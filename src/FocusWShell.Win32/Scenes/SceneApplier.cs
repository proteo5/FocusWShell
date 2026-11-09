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

/// <summary>Qué ventana va en cada rol del layout fijo de M0.</summary>
public sealed record SceneAssignment(
    IReadOnlyList<WindowSnapshot> Context,
    WindowSnapshot? Center,
    IReadOnlyList<WindowSnapshot> Work)
{
    public IEnumerable<WindowSnapshot> All =>
        Context.Concat(Center is null ? [] : [Center]).Concat(Work);
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

/// <summary>Una ventana de la escena y cómo quedó; <c>Result</c> es <c>null</c> si se minimizó.</summary>
public sealed record AppliedWindow(WindowTarget Target, PlacementResult? Result);

/// <summary>Una pasada de colocación.</summary>
/// <param name="Learned">Mínimos aprendidos en esta pasada; si hay alguno, sigue otra pasada.</param>
public sealed record ApplyPass(
    int Number,
    SceneLayoutResult Layout,
    IReadOnlyList<AppliedWindow> Applied,
    TimeSpan MoveTime,
    BatchPlacement? Stats,
    IReadOnlyList<string> Learned);

public sealed record SceneApplyReport(
    SceneLayoutResult Layout,
    IReadOnlyList<AppliedWindow> Applied,
    IReadOnlyList<ApplyPass> Passes,
    string? Error)
{
    public int ExactCount => Applied.Count(a => a.Result is null || a.Result.IsExact);

    /// <summary>Ventanas de panel cuyo rectángulo final toca el Centro.</summary>
    public IReadOnlyList<string> CenterInvaders => Applied
        .Where(a => a.Result is not null && a.Target.Region != "Centro")
        .Where(a => !a.Result!.Actual.Intersect(Layout.CenterColumn).IsEmpty)
        .Select(a => a.Target.Label)
        .ToList();

    public bool Succeeded => Error is null && ExactCount == Applied.Count && CenterInvaders.Count == 0;
}

/// <summary>
/// Aplica el layout fijo Contexto | Centro | Trabajo a un monitor: calcula con el motor,
/// escribe el diario antes de mover, mueve, aprende mínimos y recoloca si hace falta.
/// </summary>
public sealed class SceneApplier
{
    private readonly DisplayInfo _display;
    private readonly SceneAssignment _assignment;
    private readonly SceneOptions _options;
    private readonly MinSizeStore _minSizes;
    private readonly Dictionary<nint, WindowSnapshot> _byHandle;

    public SceneApplier(DisplayInfo display, SceneAssignment assignment, SceneOptions options, MinSizeStore minSizes)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _assignment = assignment;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _minSizes = minSizes ?? throw new ArgumentNullException(nameof(minSizes));
        _byHandle = assignment.All.ToDictionary(w => w.Handle);
        Catalog = minSizes.Load();
    }

    public MinSizeCatalog Catalog { get; }

    public SceneLayoutResult Plan()
    {
        LayoutWindow ToLayout(WindowSnapshot w) =>
            new(w.Handle, Path.GetFileNameWithoutExtension(w.ProcessName), Catalog.Get(MinSizeCatalog.KeyFor(w), _display.Dpi));

        return SceneLayoutEngine.Compute(new SceneInput(
            _display.WorkArea,
            new PanelInput("Contexto", PanelSide.Left, _options.ContextWeight, _options.ContextTemplate,
                _assignment.Context.Select(ToLayout).ToList()),
            _options.CenterWeight,
            _assignment.Center is null ? null : ToLayout(_assignment.Center),
            new PanelInput("Trabajo", PanelSide.Right, _options.WorkWeight, _options.WorkTemplate,
                _assignment.Work.Select(ToLayout).ToList()),
            _options.Gap));
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
        SceneLayoutResult layout, out TimeSpan moveTime, out BatchPlacement? stats, Action<int>? beforeEach = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_options.Mode == MoveMode.Sequential)
        {
            stats = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var list = new List<AppliedWindow>();
            for (int i = 0; i < layout.Targets.Count; i++)
            {
                beforeEach?.Invoke(i);
                var t = layout.Targets[i];
                if (t.Kind == TargetKind.Minimized)
                {
                    WindowMover.Minimize(t.Handle);
                    list.Add(new AppliedWindow(t, null));
                }
                else
                {
                    list.Add(new AppliedWindow(t, WindowMover.Place(t.Handle, t.Rect)));
                }
            }

            moveTime = clock.Elapsed;
            return list;
        }

        // Primero minimizar las ocultas de las pilas; luego mover las visibles juntas (R3).
        foreach (var t in layout.Targets.Where(t => t.Kind == TargetKind.Minimized))
        {
            WindowMover.Minimize(t.Handle);
        }

        var visible = layout.Targets.Where(t => t.Kind == TargetKind.Visible).ToList();
        stats = WindowMover.PlaceBatch(visible.Select(t => (t.Handle, t.Rect)).ToList(), _options.Mode == MoveMode.Async);
        var results = visible.Zip(stats.Results).ToDictionary(x => x.First.Handle, x => x.Second);
        moveTime = stats.MoveTime;
        return layout.Targets
            .Select(t => new AppliedWindow(t, t.Kind == TargetKind.Visible ? results[t.Handle] : null))
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
            if (result is { Error: null } r && _byHandle.TryGetValue(target.Handle, out var w))
            {
                var key = MinSizeCatalog.KeyFor(w);
                if (Catalog.Observe(key, new PixelSize(r.Target.Width, r.Target.Height), new PixelSize(r.Actual.Width, r.Actual.Height), _display.Dpi))
                {
                    learned.Add($"{target.Label} → mínimo {Catalog.Get(key, _display.Dpi)}");
                }
            }
        }

        return learned;
    }
}
