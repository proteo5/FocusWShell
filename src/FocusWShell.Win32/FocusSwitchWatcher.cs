using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>Cómo llegó una ventana al primer plano.</summary>
public enum SwitchGesture
{
    /// <summary>Alt+Tab.</summary>
    AltTab,

    /// <summary>Vista de tareas (Win+Tab).</summary>
    TaskView,

    /// <summary>Clic en un botón de la barra de tareas.</summary>
    Taskbar,

    /// <summary>Clic en la propia ventana.</summary>
    Click,

    /// <summary>Cualquier otra cosa: la app se activó sola, un diálogo, el propio gestor…</summary>
    Other,
}

/// <summary>
/// Detecta qué gesto del usuario trajo cada ventana al primer plano, mirando qué tuvo el primer plano
/// justo antes (medido en Windows 11):
/// <list type="bullet">
/// <item>Alt+Tab y la vista de tareas pasan por <c>XamlExplorerHostIslandWindow</c>; se distinguen por la
/// tecla pulsada cuando el selector aparece (Alt o Win). Windows 11 no emite SWITCHSTART/END; si
/// llegan (Windows 10), también cuentan como Alt+Tab.</item>
/// <item>La barra de tareas pasa por <c>Shell_TrayWnd</c> / <c>Shell_SecondaryTrayWnd</c>.</item>
/// <item>Un clic: el botón del ratón está pulsado al activarse.</item>
/// </list>
/// Las ventanas de paso (<c>ForegroundStaging</c>) se ignoran. Tras un cambio, el selector vuelve a tomar
/// el primer plano un instante al cerrarse: ese eco se descarta para que una activación posterior (de una
/// app o del propio gestor) no parezca un gesto (CA7). Volver a la ventana que ya estaba (Esc en el
/// selector) tampoco cuenta. El callback se ejecuta en el hilo del observador.
/// </summary>
public sealed class FocusSwitchWatcher : IDisposable
{
    private const uint EventForeground = 0x0003;
    private const uint EventSwitchStart = 0x0014;
    private const uint EventSwitchEnd = 0x0015;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.Ordinal) { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>Selector de Alt+Tab y vista de tareas en Windows 11.</summary>
    private const string SwitcherClass = "XamlExplorerHostIslandWindow";

    /// <summary>Ventanas de paso que Windows activa un instante durante un cambio.</summary>
    private const string StagingClass = "ForegroundStaging";

    /// <summary>Un clic en la barra de tareas tiene que activar la ventana enseguida.</summary>
    private static readonly TimeSpan TaskbarWindow = TimeSpan.FromSeconds(2);

    /// <summary>El eco del selector llega pocos milisegundos después de la ventana elegida.</summary>
    private static readonly TimeSpan EchoWindow = TimeSpan.FromMilliseconds(400);

    private readonly Action<nint, SwitchGesture> _onSwitch;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<UnhookWinEventSafeHandle> _hooks = [];
    private WINEVENTPROC? _callback;
    private uint _threadId;

    // Sólo en el hilo del observador.
    private bool _switching;
    private SwitchGesture? _pending;
    private TimeSpan _pendingSince;
    private nint _lastWindow;
    private TimeSpan _lastGestureAt = TimeSpan.FromDays(-1);

    private FocusSwitchWatcher(Action<nint, SwitchGesture> onSwitch)
    {
        _onSwitch = onSwitch;
        _thread = new Thread(Run) { IsBackground = true, Name = "FocusWShell focus switches" };
        _thread.Start();
        _ready.Wait();
    }

    public static FocusSwitchWatcher Start(Action<nint, SwitchGesture> onSwitch)
    {
        ArgumentNullException.ThrowIfNull(onSwitch);
        return new FocusSwitchWatcher(onSwitch);
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        _callback = OnEvent;
        foreach (var (min, max) in new[] { (EventForeground, EventForeground), (EventSwitchStart, EventSwitchEnd) })
        {
            var hook = PInvoke.SetWinEventHook(min, max, default, _callback, 0, 0, 0);
            if (!hook.IsInvalid)
            {
                _hooks.Add(hook);
            }
            else
            {
                hook.Dispose();
            }
        }

        _ready.Set();
        try
        {
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
            {
            }
        }
        finally
        {
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }
        }
    }

    private void OnEvent(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        switch (@event)
        {
            case EventSwitchStart:
                _switching = true;
                Arm(SwitchGesture.AltTab);
                return;
            case EventSwitchEnd:
                _switching = false;
                return;
            case EventForeground when !hwnd.IsNull:
                OnForeground((nint)(IntPtr)hwnd, ClassOf(hwnd));
                return;
        }
    }

    private void OnForeground(nint hwnd, string cls)
    {
        var now = _clock.Elapsed;
        if (cls == StagingClass)
        {
            return;
        }

        if (cls == SwitcherClass)
        {
            if (now - _lastGestureAt > EchoWindow)
            {
                Arm(IsDown(VkLWin) || IsDown(VkRWin) || !IsDown(VkMenu) ? SwitchGesture.TaskView : SwitchGesture.AltTab);
            }

            return;
        }

        if (TaskbarClasses.Contains(cls))
        {
            Arm(SwitchGesture.Taskbar);
            return;
        }

        var gesture = _pending switch
        {
            SwitchGesture.Taskbar when now - _pendingSince > TaskbarWindow => Fallback(),
            { } armed => armed,
            null when _switching => SwitchGesture.AltTab,
            null => Fallback(),
        };
        _pending = null;

        // Esc en el selector, o un clic en la barra que vuelve a la misma ventana: no es un cambio.
        if (gesture is SwitchGesture.AltTab or SwitchGesture.TaskView or SwitchGesture.Taskbar && hwnd == _lastWindow)
        {
            gesture = SwitchGesture.Other;
        }

        if (gesture != SwitchGesture.Other)
        {
            _lastGestureAt = now;
        }

        _lastWindow = hwnd;
        _onSwitch(hwnd, gesture);
    }

    private void Arm(SwitchGesture gesture)
    {
        _pending = gesture;
        _pendingSince = _clock.Elapsed;
    }

    private static SwitchGesture Fallback() =>
        IsDown(VkLButton) || IsDown(VkRButton) ? SwitchGesture.Click : SwitchGesture.Other;

    private static bool IsDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static unsafe string ClassOf(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* p = buffer)
        {
            int length = PInvoke.GetClassName(hwnd, p, buffer.Length);
            return length > 0 ? new string(buffer[..length]) : string.Empty;
        }
    }

    public void Dispose()
    {
        if (_thread.IsAlive)
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }
}
