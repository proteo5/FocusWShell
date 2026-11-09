using System.IO;
using FocusWShell.Core.Displays;
using FocusWShell.Core.Domain;
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

/// <summary>
/// Dueño del modelo (<see cref="Workspace"/>) y único punto por el que la app mueve o restaura
/// ventanas. Ejecuta los comandos y luego sus efectos, en orden: capturas y restauraciones primero,
/// un solo layout al final y, por último, el foco. Un lock serializa todo, porque los atajos llegan
/// desde otro hilo.
/// </summary>
public sealed class ShellController
{
    private readonly Lock _gate = new();
    private readonly EligibilityFilter _filter = new();
    private readonly Workspace _workspace = new();

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
        var c = _workspace.Composition;
        List<WindowSnapshot> Map(IEnumerable<nint> handles) =>
            handles.Where(_snapshots.ContainsKey).Select(h => _snapshots[h]).ToList();

        var assignment = new SceneAssignment(
            Map(c.Context),
            c.Center is { } center && _snapshots.TryGetValue(center, out var w) ? w : null,
            Map(c.Work));
        var applier = new SceneApplier(TargetDisplay(), assignment, new SceneOptions(), MinSizes);
        return applier.Apply(Journal);
    }
}
