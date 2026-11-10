using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>Un atajo Ctrl+Alt+Mayús+<paramref name="VirtualKey"/>.</summary>
/// <param name="Description">Texto para la UI, p. ej. "Ctrl+Alt+Mayús+Retroceso".</param>
/// <param name="OnPressed">Se ejecuta en el hilo de los atajos, no en el de la UI.</param>
public sealed record HotkeyBinding(string Name, uint VirtualKey, string Description, Action OnPressed);

/// <summary>
/// Atajos globales en un hilo propio con su cola de mensajes (<c>RegisterHotKey</c> sin ventana).
/// Así el de emergencia responde aunque la UI del gestor esté ocupada (03-arquitectura).
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    public const uint VkBack = 0x08;
    public const uint VkLeft = 0x25;
    public const uint VkUp = 0x26;
    public const uint VkDown = 0x28;

    private const int FirstId = 0xF0C0;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly IReadOnlyList<HotkeyBinding> _bindings;
    private readonly List<HotkeyBinding> _failed = [];
    private uint _threadId;

    private GlobalHotkeys(IReadOnlyList<HotkeyBinding> bindings)
    {
        _bindings = bindings;
        _thread = new Thread(Run) { IsBackground = true, Name = "FocusWShell hotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Atajos que Windows rechazó porque otra app ya los usa.</summary>
    public IReadOnlyList<HotkeyBinding> Failed => _failed;

    public static GlobalHotkeys Start(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return new GlobalHotkeys(bindings);
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        const HOT_KEY_MODIFIERS Modifiers =
            HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        var registered = new List<int>();
        for (int i = 0; i < _bindings.Count; i++)
        {
            if (PInvoke.RegisterHotKey(HWND.Null, FirstId + i, Modifiers, _bindings[i].VirtualKey))
            {
                registered.Add(FirstId + i);
            }
            else
            {
                _failed.Add(_bindings[i]);
            }
        }

        _ready.Set();
        try
        {
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
            {
                int index = (int)msg.wParam.Value - FirstId;
                if (msg.message == PInvoke.WM_HOTKEY && index >= 0 && index < _bindings.Count)
                {
                    _bindings[index].OnPressed();
                }
            }
        }
        finally
        {
            foreach (int id in registered)
            {
                PInvoke.UnregisterHotKey(HWND.Null, id);
            }
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
