using System.Diagnostics;
using FocusWShell.Core.Domain;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>
/// Escucha eventos de ventanas con <c>SetWinEventHook</c> (fuera de contexto) en un hilo propio y,
/// cuando una ventana vigilada deja de recibirlos durante <c>debounce</c>, entrega su estado
/// estabilizado. No decide si el cambio fue del usuario: eso lo hace <see cref="ExpectationRegistry"/>.
/// Mientras haya un <see cref="OwnChange"/> abierto no entrega nada: el gestor está moviendo.
/// </summary>
public sealed class WindowEventWatcher : IDisposable
{
    private const uint EventMoveSizeStart = 0x000A;
    private const uint EventMoveSizeEnd = 0x000B;
    private const uint EventMinimizeStart = 0x0016;
    private const uint EventMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectHide = 0x8003;
    private const uint EventLocationChange = 0x800B;
    private const int ObjIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint OutOfContextSkipOwnProcess = 0x0000 | 0x0002;

    private readonly Func<nint, bool> _isWatched;
    private readonly Action<ObservedWindow> _onSettled;
    private readonly TimeSpan _debounce;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Sólo se tocan en el hilo del observador (callbacks y temporizador comparten hilo).
    private readonly Dictionary<nint, Pending> _pending = [];
    private readonly List<UnhookWinEventSafeHandle> _hooks = [];
    private WINEVENTPROC? _callback;

    private uint _threadId;
    private int _ownChanges;
    private long _rawEvents;

    private WindowEventWatcher(Func<nint, bool> isWatched, Action<ObservedWindow> onSettled, TimeSpan debounce)
    {
        _isWatched = isWatched;
        _onSettled = onSettled;
        _debounce = debounce;
        _thread = new Thread(Run) { IsBackground = true, Name = "FocusWShell window events" };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Eventos recibidos de ventanas vigiladas (para medir).</summary>
    public long RawEventCount => Interlocked.Read(ref _rawEvents);

    /// <param name="isWatched">Si un handle interesa. Se llama en el hilo del observador.</param>
    /// <param name="onSettled">Estado estabilizado de una ventana vigilada. Se llama en el hilo del observador.</param>
    public static WindowEventWatcher Start(Func<nint, bool> isWatched, Action<ObservedWindow> onSettled, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(isWatched);
        ArgumentNullException.ThrowIfNull(onSettled);
        return new WindowEventWatcher(isWatched, onSettled, debounce ?? TimeSpan.FromMilliseconds(120));
    }

    /// <summary>
    /// Mientras esté abierto, no se entrega ningún estado. Antes de cerrarlo, el gestor debe
    /// registrar las nuevas expectativas: al cerrarse, las ventanas tocadas se comprueban contra ellas.
    /// </summary>
    public IDisposable OwnChange()
    {
        Interlocked.Increment(ref _ownChanges);
        return new Scope(this);
    }

    private sealed record Pending(TimeSpan LastEvent, bool Dragging);

    private sealed class Scope(WindowEventWatcher owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._ownChanges);
            }
        }
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        _callback = OnEvent;
        foreach (var (min, max) in new[]
        {
            (EventMoveSizeStart, EventMoveSizeEnd),
            (EventMinimizeStart, EventMinimizeEnd),
            (EventObjectDestroy, EventObjectHide),
            (EventLocationChange, EventLocationChange),
        })
        {
            var hook = PInvoke.SetWinEventHook(min, max, default, _callback, 0, 0, OutOfContextSkipOwnProcess);
            if (!hook.IsInvalid)
            {
                _hooks.Add(hook);
            }
            else
            {
                hook.Dispose();
            }
        }

        // Temporizador del hilo: WM_TIMER llega a esta cola y revisa qué ventanas ya se calmaron.
        nuint timer = PInvoke.SetTimer(HWND.Null, 0, 25, null);
        _ready.Set();
        try
        {
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
            {
                if (msg.message == PInvoke.WM_TIMER)
                {
                    Tick();
                }
            }
        }
        finally
        {
            PInvoke.KillTimer(HWND.Null, timer);
            // Se liberan (UnhookWinEvent) en el mismo hilo que los instaló.
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }
        }
    }

    private void OnEvent(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != ObjIdWindow || idChild != ChildIdSelf || hwnd.IsNull || !_isWatched(hwnd))
        {
            return;
        }

        Interlocked.Increment(ref _rawEvents);
        bool dragging = _pending.TryGetValue(hwnd, out var p) && p.Dragging;
        dragging = @event switch
        {
            EventMoveSizeStart => true,
            EventMoveSizeEnd => false,
            _ => dragging,
        };
        _pending[hwnd] = new Pending(_clock.Elapsed, dragging);
    }

    private void Tick()
    {
        if (Volatile.Read(ref _ownChanges) > 0 || _pending.Count == 0)
        {
            return;
        }

        var now = _clock.Elapsed;
        foreach (var (handle, p) in _pending.Where(x => !x.Value.Dragging && now - x.Value.LastEvent >= _debounce).ToList())
        {
            _pending.Remove(handle);
            _onSettled(WindowMover.Observe(handle));
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
