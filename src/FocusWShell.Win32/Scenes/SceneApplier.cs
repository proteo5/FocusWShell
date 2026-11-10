using FocusWShell.Core.Displays;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Core.Windows;
using FocusWShell.Win32.Persistence;

namespace FocusWShell.Win32.Scenes;

/// <summary>Cómo se mueven las ventanas de una escena (S3).</summary>
public enum MoveMode
{
    /// <summary><c>SWP_ASYNCWINDOWPOS</c> a todas y espera a que lleguen. Es el modo del aplicador.</summary>
    Async,

    /// <summary>Un lote <c>DeferWindowPos</c>. Sólo para comparar: no agrupa entre procesos.</summary>
    Batch,

    /// <summary><c>SetWindowPos</c> una a una.</summary>
    Sequential,
}

/// <summary>Paneles de un monitor auxiliar (ADR 0008).</summary>
public sealed record AuxAssignment(
    DisplayInfo Display,
    IReadOnlyList<WindowSnapshot> Context,
    IReadOnlyList<WindowSnapshot> Work,
    PanelTemplate ContextTemplate = PanelTemplate.Tiles,
    PanelTemplate WorkTemplate = PanelTemplate.Tiles);

/// <summary>Qué ventana va en cada rol: Contexto | Centro | Trabajo en el principal y, opcionalmente, paneles en auxiliares.</summary>
public sealed record SceneAssignment(
    IReadOnlyList<WindowSnapshot> Context,
    WindowSnapshot? Center,
    IReadOnlyList<WindowSnapshot> Work)
{
    public IReadOnlyList<AuxAssignment> Aux { get; init; } = [];

    public IEnumerable<WindowSnapshot> All =>
        Context.Concat(Center is null ? [] : [Center]).Concat(Work)
            .Concat(Aux.SelectMany(a => a.Context.Concat(a.Work)));
}

public sealed record SceneOptions
{
    public double ContextWeight { get; init; } = 18;
    public double CenterWeight { get; init; } = 55;
    public double WorkWeight { get; init; } = 27;
    public int Gap { get; init; }
    public PanelTemplate ContextTemplate { get; init; } = PanelTemplate.Tiles;
    public PanelTemplate WorkTemplate { get; init; } = PanelTemplate.Tiles;
    public MoveMode Mode { get; init; } = MoveMode.Async;

    /// <summary>Si una app impone un tamaño mínimo nuevo, aprenderlo y recolocar.</summary>
    public bool Learn { get; init; } = true;

    public int MaxPasses { get; init; } = 3;
}

/// <summary>Plan de toda la escena: el monitor principal y cada auxiliar.</summary>
public sealed record ScenePlan(SceneLayoutResult Principal, IReadOnlyList<AuxLayoutResult> Aux)
{
    public IReadOnlyList<WindowTarget> Targets => [.. Principal.Targets, .. Aux.SelectMany(a => a.Targets)];

    public IReadOnlyList<string> Notes => [.. Principal.Notes, .. Aux.SelectMany(a => a.Notes)];
}

/// <summary>Una ventana de la escena y cómo quedó; <c>Result</c> es <c>null</c> si se minimizó.</summary>
public sealed record AppliedWindow(WindowTarget Target, PlacementResult? Result);

/// <summary>Una pasada de colocación.</summary>
/// <param name="Learned">Mínimos aprendidos en esta pasada; si hay alguno, sigue otra pasada.</param>
public sealed record ApplyPass(
    int Number,
    ScenePlan Layout,
    IReadOnlyList<AppliedWindow> Applied,
    TimeSpan MoveTime,
    BatchPlacement? Stats,
    IReadOnlyList<string> Learned);

public sealed record SceneApplyReport(
    ScenePlan Layout,
    IReadOnlyList<AppliedWindow> Applied,
    IReadOnlyList<ApplyPass> Passes,
    string? Error)
{
    public int ExactCount => Applied.Count(a => a.Result is null || a.Result.IsExact);

    /// <summary>Ventanas de panel cuyo rectángulo final toca el Centro.</summary>
    public IReadOnlyList<string> CenterInvaders => Applied
        .Where(a => a.Result is not null && a.Target.Region != "Centro")
        .Where(a => !a.Result!.Actual.Intersect(Layout.Principal.CenterColumn).IsEmpty)
        .Select(a => a.Target.Label)
        .ToList();

    public bool Succeeded => Error is null && ExactCount == Applied.Count && CenterInvaders.Count == 0;
}

/// <summary>
/// Aplica la escena: Contexto | Centro | Trabajo en el monitor principal y paneles en los auxiliares.
/// Calcula con los motores, escribe el diario antes de mover, mueve todo junto, aprende mínimos y
/// recoloca si hace falta.
/// </summary>
public sealed class SceneApplier
{
    private readonly DisplayInfo _display;
    private readonly SceneAssignment _assignment;
    private readonly SceneOptions _options;
    private readonly MinSizeStore _minSizes;
    private readonly Dictionary<nint, WindowSnapshot> _byHandle;

    /// <summary>DPI del monitor de destino de cada ventana, para el catálogo de mínimos.</summary>
    private readonly Dictionary<nint, int> _dpiByHandle = [];

    public SceneApplier(DisplayInfo display, SceneAssignment assignment, SceneOptions options, MinSizeStore minSizes)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _assignment = assignment;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _minSizes = minSizes ?? throw new ArgumentNullException(nameof(minSizes));
        _byHandle = assignment.All.ToDictionary(w => w.Handle);
        foreach (var w in assignment.Context.Concat(assignment.Work).Concat(assignment.Center is null ? [] : [assignment.Center]))
        {
            _dpiByHandle[w.Handle] = display.Dpi;
        }

        foreach (var aux in assignment.Aux)
        {
            foreach (var w in aux.Context.Concat(aux.Work))
            {
                _dpiByHandle[w.Handle] = aux.Display.Dpi;
            }
        }

        Catalog = minSizes.Load();
    }

    public MinSizeCatalog Catalog { get; }

    public ScenePlan Plan()
    {
        LayoutWindow ToLayout(WindowSnapshot w, int dpi) =>
            new(w.Handle, Path.GetFileNameWithoutExtension(w.ProcessName), Catalog.Get(MinSizeCatalog.KeyFor(w), dpi));

        // Un panel cuyo borde exterior da a otro monitor no puede desbordar hacia fuera.
        var shared = DisplayTopology.SharedEdges(_display, DisplayEnumerator.Enumerate());
        int dpi = _display.Dpi;
        var principal = SceneLayoutEngine.Compute(new SceneInput(
            _display.WorkArea,
            new PanelInput("Contexto", PanelSide.Left, _options.ContextWeight, _options.ContextTemplate,
                _assignment.Context.Select(w => ToLayout(w, dpi)).ToList(), shared.Left),
            _options.CenterWeight,
            _assignment.Center is null ? null : ToLayout(_assignment.Center, dpi),
            new PanelInput("Trabajo", PanelSide.Right, _options.WorkWeight, _options.WorkTemplate,
                _assignment.Work.Select(w => ToLayout(w, dpi)).ToList(), shared.Right),
            _options.Gap));

        var aux = _assignment.Aux
            .Select(a => AuxLayoutEngine.Compute(new AuxInput(
                a.Display.DeviceName,
                a.Display.WorkArea,
                [
                    new PanelInput("Contexto", PanelSide.Left, 1, a.ContextTemplate, a.Context.Select(w => ToLayout(w, a.Display.Dpi)).ToList()),
                    new PanelInput("Trabajo", PanelSide.Right, 1, a.WorkTemplate, a.Work.Select(w => ToLayout(w, a.Display.Dpi)).ToList()),
                ],
                _options.Gap)))
            .ToList();

        return new ScenePlan(principal, aux);
    }

    /// <summary>
    /// Escribe el diario, mueve y, si aprende mínimos nuevos, recalcula y recoloca.
    /// <paramref name="beforeEach"/> sólo se usa en modo secuencial (S5: pausar a mitad).
    /// </summary>
    public SceneApplyReport Apply(RecoveryJournal journal, Action<ApplyPass>? onPass = null, Action<int>? beforeEach = null)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var layout = Plan();
        var all = _assignment.All.ToList();

        // Diario ANTES de mover (ADR 0005).
        var captures = all
            .Select(w => WindowMover.Capture(w.Handle, w.ProcessId, w.ProcessName, w.Title))
            .OfType<SavedPlacement>()
            .ToList();
        if (captures.Count != all.Count)
        {
            return new SceneApplyReport(layout, [], [], "No se pudo capturar el estado original de todas las ventanas; no se movió nada.");
        }

        journal.AddIfMissing(captures);

        var passes = new List<ApplyPass>();
        IReadOnlyList<AppliedWindow> applied = [];
        for (int number = 1; number <= _options.MaxPasses; number++)
        {
            applied = Move(layout, out var moveTime, out var stats, beforeEach);
            var learned = _options.Learn ? Learn(applied) : [];
            var pass = new ApplyPass(number, layout, applied, moveTime, stats, learned);
            passes.Add(pass);
            onPass?.Invoke(pass);
            if (learned.Count == 0)
            {
                break;
            }

            _minSizes.Save(Catalog);
            layout = Plan();
        }

        return new SceneApplyReport(layout, applied, passes, null);
    }

    /// <summary>Mueve las ventanas a <paramref name="layout"/> sin tocar el diario.</summary>
    public IReadOnlyList<AppliedWindow> Move(
        ScenePlan layout, out TimeSpan moveTime, out BatchPlacement? stats, Action<int>? beforeEach = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var targets = layout.Targets;
        if (_options.Mode == MoveMode.Sequential)
        {
            stats = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var list = new List<AppliedWindow>();
            for (int i = 0; i < targets.Count; i++)
            {
                beforeEach?.Invoke(i);
                var t = targets[i];
                switch (t.Kind)
                {
                    case TargetKind.Minimized:
                        WindowMover.Minimize(t.Handle);
                        list.Add(new AppliedWindow(t, null));
                        break;
                    case TargetKind.Maximized:
                        list.Add(new AppliedWindow(t, WindowMover.MaximizeOn(t.Handle, t.Rect)));
                        break;
                    default:
                        list.Add(new AppliedWindow(t, WindowMover.Place(t.Handle, t.Rect)));
                        break;
                }
            }

            moveTime = clock.Elapsed;
            return list;
        }

        // 1. Minimizar las ocultas. 2. Mover juntas las visibles y las que hay que maximizar (éstas, al
        //    rectángulo del monitor, para que crucen de monitor sin activarse). 3. Maximizar.
        foreach (var t in targets.Where(t => t.Kind == TargetKind.Minimized))
        {
            WindowMover.Minimize(t.Handle);
        }

        var moving = targets
            .Where(t => t.Kind == TargetKind.Visible || (t.Kind == TargetKind.Maximized && !WindowMover.IsMaximizedOn(t.Handle, t.Rect)))
            .ToList();
        stats = WindowMover.PlaceBatch(moving.Select(t => (t.Handle, t.Rect)).ToList(), _options.Mode == MoveMode.Async);
        var results = moving.Zip(stats.Results).ToDictionary(x => x.First.Handle, x => x.Second);
        foreach (var t in targets.Where(t => t.Kind == TargetKind.Maximized))
        {
            results[t.Handle] = WindowMover.MaximizeOn(t.Handle, t.Rect);
        }

        moveTime = stats.MoveTime;
        return targets
            .Select(t => new AppliedWindow(t, t.Kind == TargetKind.Minimized ? null : results[t.Handle]))
            .ToList();
    }

    /// <summary>Mueve una lista de ventanas a rectángulos dados con el modo configurado (ciclos de S3).</summary>
    public TimeSpan MoveTo(IReadOnlyList<(nint Handle, PixelRect Rect)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_options.Mode != MoveMode.Sequential)
        {
            return WindowMover.PlaceBatch(items, _options.Mode == MoveMode.Async).MoveTime;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (h, r) in items)
        {
            WindowMover.Place(h, r);
        }

        return clock.Elapsed;
    }

    private List<string> Learn(IReadOnlyList<AppliedWindow> applied)
    {
        var learned = new List<string>();
        foreach (var (target, result) in applied)
        {
            // Una ventana maximizada no enseña nada sobre su tamaño mínimo.
            if (target.Kind == TargetKind.Visible && result is { Error: null } r && _byHandle.TryGetValue(target.Handle, out var w))
            {
                int dpi = _dpiByHandle.GetValueOrDefault(target.Handle, _display.Dpi);
                var key = MinSizeCatalog.KeyFor(w);
                if (Catalog.Observe(key, new PixelSize(r.Target.Width, r.Target.Height), new PixelSize(r.Actual.Width, r.Actual.Height), dpi))
                {
                    learned.Add($"{target.Label} → mínimo {Catalog.Get(key, dpi)}");
                }
            }
        }

        return learned;
    }
}
