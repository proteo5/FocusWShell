using FocusWShell.Core.Geometry;
using FocusWShell.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace FocusWShell.Win32;

/// <summary>
/// Vigila si la ventana en primer plano está en pantalla completa propia (M2): F11, un vídeo, una
/// presentación, un juego. Cada medio segundo mira el primer plano; avisa (en un hilo del grupo) con el
/// rectángulo del monitor que ocupa, o con <c>null</c> cuando deja de haberla.
/// </summary>
public sealed class AppFullscreenWatcher : IDisposable
{
    /// <summary>Lo que no es una app: el escritorio y la barra de tareas.</summary>
    private static readonly HashSet<string> Shell = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly Action<PixelRect?> _onChange;
    private readonly Timer _timer;
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private PixelRect? _current;
    private int _ticking;

    private AppFullscreenWatcher(Action<PixelRect?> onChange)
    {
        _onChange = onChange;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public static AppFullscreenWatcher Start(Action<PixelRect?> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        return new AppFullscreenWatcher(onChange);
    }

    private unsafe void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            var found = Detect(PInvoke.GetForegroundWindow());
            if (found != _current)
            {
                _current = found;
                _onChange(found);
            }
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private unsafe PixelRect? Detect(HWND fg)
    {
        if (fg.IsNull)
        {
            return null;
        }

        uint pid;
        _ = PInvoke.GetWindowThreadProcessId(fg, &pid);
        if (pid == _ownProcess || Shell.Contains(ClassOf(fg)))
        {
            return null;
        }

        var observed = WindowMover.Observe((nint)(IntPtr)fg);
        if (!observed.Exists || observed.Minimized)
        {
            return null;
        }

        var monitor = WindowEnumerator.MonitorBoundsOf((nint)(IntPtr)fg);
        return AppFullscreen.Covers(observed.Visible, monitor, observed.Maximized) ? monitor : null;
    }

    private static unsafe string ClassOf(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* p = buffer)
        {
            int length = PInvoke.GetClassName(hwnd, p, buffer.Length);
            return length > 0 ? new string(buffer[..length]) : string.Empty;
        }
    }

    public void Dispose() => _timer.Dispose();
}
