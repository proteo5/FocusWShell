using System.IO;
using FocusWShell.Core.Displays;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;
using FocusWShell.Core.Windows;
using FocusWShell.Win32;
using FocusWShell.Win32.Persistence;
using FocusWShell.Win32.Scenes;

namespace FocusWShell.App;

/// <summary>Lo que pasó al ejecutar uno o varios comandos.</summary>
public sealed record ControllerResult(
    IReadOnlyList<string> Rejections,
    IReadOnlyList<RestoreReport> Restores,
    SceneApplyReport? Applied);

/// <summary>Un cambio que hizo el usuario (u otra app) y lo que el gestor hizo con él.</summary>
public sealed record ExternalChangeHandled(string Label, ExternalChange Change, IReadOnlyList<Command> Commands, ControllerResult Result)
{
    public string Describe() => (Change, Commands) switch
    {
        (ExternalChange.Minimized, [Park]) => $"{Label}: la minimizaste; queda estacionada en su panel.",
        (ExternalChange.Shown, [Restore]) => $"{Label}: la abriste desde la barra de tareas; vuelve a su panel.",
        (ExternalChange.Shown, [MakeActiveInPanel]) => $"{Label}: la abriste; pasa a ser la visible de su panel.",
        (ExternalChange.Maximized, [MakeActiveInPanel, SetTemplate]) => $"{Label}: la maximizaste en el monitor auxiliar; su panel pasa a Fill.",
        (ExternalChange.Unmaximized, [SetTemplate]) => $"{Label}: dejó de estar maximizada; su panel vuelve a Tiles.",
        (ExternalChange.Closed, _) => $"{Label}: se cerró; deja de gestionarse.",
        (_, [Detach]) => $"{Label}: la moviste tú; queda fuera de layout hasta que la reencajes.",
        _ => $"{Label}: {Change}.",
    };
}

/// <summary>
/// Dueño del modelo (<see cref="Workspace"/>) y único punto por el que la app mueve o restaura
/// ventanas. Ejecuta los comandos y luego sus efectos, en orden: capturas y restauraciones primero,
/// un solo layout al final y, por último, el foco. Un lock serializa todo, porque los atajos y los
/// eventos de ventanas llegan desde otros hilos.
/// Además observa las ventanas gestionadas (S4, ADR 0007) y traduce lo que haga el usuario en
/// comandos: minimizar estaciona, arrastrar deja fuera de layout, etc.
/// </summary>
public sealed class ShellController : IDisposable
{
    private readonly Lock _gate = new();
    private readonly EligibilityFilter _filter = new();
    private readonly Workspace _workspace = new();
    private readonly ExpectationRegistry _expectations = new();
    private WindowEventWatcher? _watcher;

    /// <summary>Se dispara (en un hilo de fondo) cada vez que el gestor reacciona a un cambio externo.</summary>
    public event Action<ExternalChangeHandled>? ExternalChangeHandled;

    /// <summary>Empieza a observar las ventanas gestionadas.</summary>
    public void StartWatching()
    {
        _watcher ??= WindowEventWatcher.Start(
            _expectations.IsWatched,
            observed => Task.Run(() => OnSettled(observed.Handle)));
    }

    public void Dispose() => _watcher?.Dispose();

    /// <summary>
    /// Una ventana vigilada se calmó. Dentro del lock se vuelve a leer su estado: si entretanto el
    /// gestor la movió, la observación del observador ya no vale.
    /// </summary>
    private void OnSettled(nint handle)
    {
        ExternalChangeHandled? handled = null;
        lock (_gate)
        {
            var change = _expectations.Reconcile(WindowMover.Observe(handle));
            if (change == ExternalChange.None)
            {
                return;
            }

            var label = _workspace.Find(handle)?.Label ?? "?";
            var commands = _workspace.Interpret(handle, change);
            var result = commands.Count > 0 ? RunLocked(commands) : new ControllerResult([], [], null);
            handled = new ExternalChangeHandled(label, change, commands, result);
        }

        ExternalChangeHandled?.Invoke(handled);
    }

    /// <summary>Última foto de cada ventana conocida: el layout y el diario necesitan proceso y título.</summary>
    private readonly Dictionary<nint, WindowSnapshot> _snapshots = [];

    public RecoveryJournal Journal { get; } = new(RecoveryJournal.DefaultPath);

    public MinSizeStore MinSizes { get; } = new(MinSizeStore.DefaultPath);

    /// <summary>Monitor del Centro: el principal de Windows (ADR 0008).</summary>
    public static DisplayInfo TargetDisplay() =>
        DisplayTopology.Principal(DisplayEnumerator.Enumerate())
        ?? throw new InvalidOperationException("No se encontró ningún monitor.");

    public static string LabelFor(WindowSnapshot w)
    {
        ArgumentNullException.ThrowIfNull(w);
        return Path.GetFileNameWithoutExtension(w.ProcessName);
    }

    public int PendingCount => Journal.Load().Count;

    /// <summary>Copia del estado del modelo, para pintar la UI.</summary>
    public IReadOnlyList<ManagedWindow> Managed()
    {
        lock (_gate)
        {
            return [.. _workspace.Windows];
        }
    }

    /// <summary>
    /// Ventanas que el gestor puede colocar, en orden Z. Las gestionadas que ya no aparecen se
    /// dan por cerradas (en M0 aún no hay hooks de eventos; S4 llega en M1).
    /// </summary>
    public IReadOnlyList<WindowSnapshot> Refresh()
    {
        var own = WindowEnumerator.CurrentIntegrity();
        var windows = WindowEnumerator.EnumerateTopLevel()
            .Where(w => _filter.Evaluate(w, own) is { IsCandidate: true, Manageable: true })
            .ToList();

        lock (_gate)
        {
            foreach (var w in windows)
            {
                _snapshots[w.Handle] = w;
            }

            var alive = windows.Select(w => w.Handle).ToHashSet();
            var closed = _workspace.Windows.Where(m => !alive.Contains(m.Handle)).Select(m => new WindowClosed(m.Handle)).ToList();
            if (closed.Count > 0)
            {
                RunLocked(closed);
            }
        }

        return windows;
    }

    public ControllerResult Execute(params Command[] commands) => Execute((IReadOnlyList<Command>)commands);

    public ControllerResult Execute(IReadOnlyList<Command> commands)
    {
        lock (_gate)
        {
            return RunLocked(commands);
        }
    }

    /// <summary><c>RestoreAll</c>: vacía el modelo y aplica todo el diario, sin motor de layout.</summary>
    public RestoreReport RestoreAllWindows() =>
        Execute(new RestoreAll()).Restores is [var report, ..] ? report : new RestoreReport([]);

    /// <summary>Promueve la ventana en primer plano, si está gestionada (atajo).</summary>
    public ControllerResult PromoteForeground()
    {
        var foreground = WindowMover.Foreground();
        lock (_gate)
        {
            return _workspace.IsManaged(foreground)
                ? RunLocked([new Promote(foreground)])
                : new ControllerResult(["La ventana activa no está gestionada por FocusWShell."], [], null);
        }
    }

    /// <summary>Devuelve el Centro a su panel (atajo).</summary>
    public ControllerResult ReturnCenter()
    {
        lock (_gate)
        {
            return _workspace.Center is { } c
                ? RunLocked([new ReturnToPanel(c.Handle)])
                : new ControllerResult(["El Centro está vacío."], [], null);
        }
    }

    private ControllerResult RunLocked(IReadOnlyList<Command> commands)
    {
        // Lo que el gestor mueva aquí no es del usuario: el observador calla hasta que haya
        // expectativas nuevas (ADR 0007).
        using var own = _watcher?.OwnChange();
        var result = RunEffectsLocked(commands);
        SyncExpectations(result.Applied);
        return result;
    }

    /// <summary>
    /// Deja como expectativa lo que el aplicador verificó. Las ventanas que el layout no tocó (fuera de
    /// layout, o sin layout en este comando) se vigilan desde su estado actual si aún no tenían
    /// expectativa; las que dejaron de gestionarse se olvidan.
    /// </summary>
    private void SyncExpectations(SceneApplyReport? applied)
    {
        var managed = _workspace.Windows.Select(w => w.Handle).ToHashSet();
        foreach (var handle in _snapshots.Keys.Where(h => !managed.Contains(h) && _expectations.IsWatched(h)).ToList())
        {
            _expectations.Forget(handle);
        }

        var placed = new HashSet<nint>();
        foreach (var (target, result) in applied?.Applied ?? [])
        {
            placed.Add(target.Handle);
            _expectations.Expect(target.Handle, (target.Kind, result) switch
            {
                (_, null) => new ExpectMinimized(),
                (TargetKind.Maximized, _) => new ExpectMaximized(target.Rect),
                _ => new ExpectVisible(result.Actual),
            });
        }

        foreach (var w in _workspace.Windows.Where(w => !placed.Contains(w.Handle)))
        {
            if (w.Detached || !_expectations.IsWatched(w.Handle))
            {
                var o = WindowMover.Observe(w.Handle);
                _expectations.Expect(w.Handle, o switch
                {
                    { Minimized: true } => new ExpectMinimized(),
                    { Maximized: true } => new ExpectMaximized(o.Visible),
                    _ => new ExpectVisible(o.Visible),
                });
            }
        }
    }

    private ControllerResult RunEffectsLocked(IReadOnlyList<Command> commands)
    {
        var rejections = new List<string>();
        var effects = new List<Effect>();
        foreach (var command in commands)
        {
            var outcome = _workspace.Execute(command);
            if (outcome.Accepted)
            {
                effects.AddRange(outcome.Effects);
            }
            else
            {
                rejections.Add(outcome.Rejection!);
            }
        }

        var restores = new List<RestoreReport>();
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case CaptureOriginal c when _snapshots.TryGetValue(c.Handle, out var w):
                    if (WindowMover.Capture(w.Handle, w.ProcessId, w.ProcessName, w.Title) is { } saved)
                    {
                        Journal.AddIfMissing([saved]);
                    }

                    break;
                case RestoreOriginal r:
                    restores.Add(JournalRestorer.Restore(Journal, e => e.Handle == r.Handle));
                    break;
                case RestoreEverything:
                    restores.Add(JournalRestorer.Restore(Journal));
                    break;
            }
        }

        SceneApplyReport? applied = null;
        if (effects.Any(e => e is ApplyLayout) && _workspace.Windows.Count > 0)
        {
            applied = ApplyLayoutLocked();
        }

        foreach (var activate in effects.OfType<Activate>())
        {
            WindowMover.Activate(activate.Handle);
        }

        return new ControllerResult(rejections, restores, applied);
    }

    private SceneApplyReport ApplyLayoutLocked()
    {
        var displays = DisplayEnumerator.Enumerate();
        var principal = DisplayTopology.Principal(displays) ?? throw new InvalidOperationException("No se encontró ningún monitor.");

        // Un auxiliar que ya no está: sus ventanas pasan a los paneles del principal (ADR 0008).
        foreach (var gone in _workspace.Composition.Aux.Select(a => a.Display).Where(d => displays.All(x => x.DeviceName != d)).ToList())
        {
            _workspace.Execute(new DisplayRemoved(gone));
        }

        var c = _workspace.Composition;
        List<WindowSnapshot> Map(IEnumerable<nint> handles) =>
            handles.Where(_snapshots.ContainsKey).Select(h => _snapshots[h]).ToList();

        var assignment = new SceneAssignment(
            Map(c.Context),
            c.Center is { } center && _snapshots.TryGetValue(center, out var w) ? w : null,
            Map(c.Work))
        {
            Parked = Map(c.Parked),
            Aux = c.Aux
                .Select(a => new AuxAssignment(
                    displays.First(d => d.DeviceName == a.Display), Map(a.Context), Map(a.Work), a.ContextTemplate, a.WorkTemplate))
                .ToList(),
        };
        var options = new SceneOptions { ContextTemplate = c.ContextTemplate, WorkTemplate = c.WorkTemplate };
        var applier = new SceneApplier(principal, assignment, options, MinSizes);
        return applier.Apply(Journal);
    }

    /// <summary>Monitores auxiliares conectados (todos menos el principal), en orden de Windows.</summary>
    public static IReadOnlyList<DisplayInfo> AuxiliaryDisplays()
    {
        var displays = DisplayEnumerator.Enumerate();
        var principal = DisplayTopology.Principal(displays);
        return displays.Where(d => d != principal).ToList();
    }

    public PanelTemplate TemplateOf(string? display, Home home)
    {
        lock (_gate)
        {
            return _workspace.TemplateOf(display, home);
        }
    }
}
