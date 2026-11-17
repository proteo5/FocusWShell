using System.Windows.Threading;
using FocusWShell.Core.Activity;
using FocusWShell.Win32;
using FocusWShell.Win32.Persistence;
using Microsoft.Win32;

namespace FocusWShell.App;

/// <summary>
/// Lleva la cuenta de la actividad (ADR 0019): le pasa al <see cref="ActivityTracker"/> los cambios de la app
/// en el foco y en uso, y cuándo se deja de tocar el equipo; escribe los tramos en el <see cref="ActivityStore"/>.
/// Vive en el hilo de la interfaz.
/// </summary>
public sealed class ActivityRecorder : IDisposable
{
    /// <summary>Sin tocar teclado ni ratón este rato, el tiempo deja de contar.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(5);

    private readonly ShellController _controller;
    private readonly ActivityTracker _tracker = new();
    private readonly DispatcherTimer _timer;
    private DateTime _lastFlush = DateTime.Now;
    private bool _paused; // bloqueado o suspendido

    public ActivityRecorder(ShellController controller, ActivityStore store)
    {
        _controller = controller;
        Store = store;
        _timer = new DispatcherTimer { Interval = Tick };
        _timer.Tick += (_, _) => OnTick();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public ActivityStore Store { get; }

    /// <summary>Se escribieron tramos nuevos (para refrescar la sección Actividad si está abierta).</summary>
    public event Action? Saved;

    public bool Enabled => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _timer.Start();
        _paused = false;
        _tracker.Active(DateTime.Now);
        Observe();
    }

    /// <summary>Deja de medir y guarda lo medido.</summary>
    public void Stop()
    {
        if (!_timer.IsEnabled)
        {
            return;
        }

        Write(_tracker.Idle(DateTime.Now));
        _timer.Stop();
    }

    /// <summary>Cambió la app en uso o lo que hay en el foco.</summary>
    public void Observe()
    {
        if (_timer.IsEnabled && !_paused)
        {
            var (focus, inUse) = _controller.ActivityNow();
            Write(_tracker.Observe(DateTime.Now, focus, inUse));
        }
    }

    /// <summary>Guarda el tramo abierto (al apagar o reiniciar, o para ver el día al momento).</summary>
    public void Flush()
    {
        if (_timer.IsEnabled)
        {
            Write(_tracker.Flush(DateTime.Now));
        }
    }

    private void OnTick()
    {
        if (_paused)
        {
            return;
        }

        var now = DateTime.Now;
        var idle = WindowEnumerator.IdleTime();
        if (idle >= IdleAfter)
        {
            if (_tracker.Running)
            {
                Write(_tracker.Idle(now - idle));
            }

            return;
        }

        if (!_tracker.Running)
        {
            _tracker.Active(now);
        }

        Observe(); // por si algo cambió sin aviso (el foco desde otro lado)
        if (now - _lastFlush >= FlushEvery)
        {
            Flush();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            Pause();
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            Resume();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Pause();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Resume();
        }
    }

    // Los eventos del sistema llegan en otro hilo: se pasan al de la interfaz.
    private void Pause() => _timer.Dispatcher.BeginInvoke(() =>
    {
        _paused = true;
        if (_timer.IsEnabled)
        {
            Write(_tracker.Idle(DateTime.Now));
        }
    });

    private void Resume() => _timer.Dispatcher.BeginInvoke(() =>
    {
        _paused = false;
        if (_timer.IsEnabled)
        {
            _tracker.Active(DateTime.Now);
            Observe();
        }
    });

    private void Write(IReadOnlyList<ActivitySegment> segments)
    {
        _lastFlush = DateTime.Now;
        if (segments.Count == 0)
        {
            return;
        }

        try
        {
            Store.Append(segments);
            Saved?.Invoke();
        }
        catch (System.IO.IOException)
        {
            // Sin disco no se pierde nada más que estos minutos de actividad.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
