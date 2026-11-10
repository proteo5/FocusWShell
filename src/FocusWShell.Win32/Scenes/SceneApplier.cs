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

/// <summary>Un panel con sus ventanas, en orden (ADR 0011).</summary>
/// <param name="Id">Identificador del panel: las regiones del plan usan este valor.</param>
/// <param name="Title">Nombre visible, para las notas.</param>
public sealed record PanelAssignment(string Id, string Title, PanelSide Side, PanelTemplate Template, IReadOnlyList<WindowSnapshot> Windows)
{
    /// <summary>Arriba o abajo en su columna.</summary>
    public PanelSlot Slot { get; init; }
}

/// <summary>Paneles de un monitor auxiliar, de izquierda a derecha (ADR 0008).</summary>
public sealed record AuxAssignment(DisplayInfo Display, IReadOnlyList<PanelAssignment> Panels)
{
    /// <summary>Atajo para el probe: un panel "Contexto" a la izquierda y uno "Trabajo" a la derecha.</summary>
    public static AuxAssignment FromSides(
        DisplayInfo display,
        IReadOnlyList<WindowSnapshot> context,
        IReadOnlyList<WindowSnapshot> work,
        PanelTemplate contextTemplate = PanelTemplate.Tiles,
        PanelTemplate workTemplate = PanelTemplate.Tiles) =>
        new(display,
        [
            new PanelAssignment("Contexto", "Contexto", PanelSide.Left, contextTemplate, context),
            new PanelAssignment("Trabajo", "Trabajo", PanelSide.Right, workTemplate, work),
        ]);
}

/// <summary>Qué ventana va en cada rol: Contexto | Centro | Trabajo en el principal y, opcionalmente, paneles en auxiliares.</summary>
public sealed record SceneAssignment(
    IReadOnlyList<WindowSnapshot> Context,
    WindowSnapshot? Center,
    IReadOnlyList<WindowSnapshot> Work)
{
    public IReadOnlyList<AuxAssignment> Aux { get; init; } = [];

    /// <summary>Ventanas estacionadas: se dejan minimizadas.</summary>
    public IReadOnlyList<WindowSnapshot> Parked { get; init; } = [];

    /// <summary>Identificador y nombre visible del panel izquierdo del principal.</summary>
    public string ContextId { get; init; } = "Contexto";

    public string ContextTitle { get; init; } = "Contexto";

    /// <summary>Identificador y nombre visible del panel derecho del principal.</summary>
    public string WorkId { get; init; } = "Trabajo";

    public string WorkTitle { get; init; } = "Trabajo";

    /// <summary>Segundo panel de la columna izquierda del principal, si lo hay.</summary>
    public PanelAssignment? ContextLower { get; init; }

    /// <summary>Segundo panel de la columna derecha del principal.</summary>
    public PanelAssignment? WorkLower { get; init; }

    /// <summary>Ventanas del principal (sin las estacionadas).</summary>
    public IEnumerable<WindowSnapshot> OnPrincipal =>
        Context.Concat(ContextLower?.Windows ?? []).Concat(Center is null ? [] : [Center]).Concat(Work).Concat(WorkLower?.Windows ?? []);

    public IEnumerable<WindowSnapshot> All =>
        OnPrincipal
            .Concat(Aux.SelectMany(a => a.Panels.SelectMany(p => p.Windows)))
            .Concat(Parked);
}

public sealed record SceneOptions
{
    public double ContextWeight { get; init; } = 18;
    public double CenterWeight { get; init; } = 55;
    public double WorkWeight { get; init; } = 27;
    public int Gap { get; init; }
    public PanelTemplate ContextTemplate { get; init; } = PanelTemplate.Tiles;
    public PanelTemplate WorkTemplate { get; init; } = PanelTemplate.Tiles;

    /// <summary>Vista del principal: dónde va el foco (ADR 0012).</summary>
    public FocusPosition Focus { get; init; }

    public MoveMode Mode { get; init; } = MoveMode.Async;

    /// <summary>Franja para la tira de pestañas arriba de cada panel, en píxeles lógicos (96 DPI). 0 = sin tiras.</summary>
    public int PanelTopInset { get; init; }

    /// <summary>Si una app impone un tamaño mínimo nuevo, aprenderlo y recolocar.</summary>
    public bool Learn { get; init; } = true;

    public int MaxPasses { get; init; } = 3;
}

/// <summary>Plan de toda la escena: el monitor principal y cada auxiliar.</summary>
public sealed record ScenePlan(SceneLayoutResult Principal, IReadOnlyList<AuxLayoutResult> Aux)
{
    /// <summary>Ventanas estacionadas (minimizadas).</summary>
    public IReadOnlyList<WindowTarget> Parked { get; init; } = [];

    public IReadOnlyList<WindowTarget> Targets => [.. Principal.Targets, .. Aux.SelectMany(a => a.Targets), .. Parked];

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
        foreach (var w in assignment.OnPrincipal)
        {
            _dpiByHandle[w.Handle] = display.Dpi;
        }

        foreach (var aux in assignment.Aux)
        {
            foreach (var w in aux.Panels.SelectMany(p => p.Windows))
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

        // Un panel cuyo borde exterior da a otro monitor no puede desbordar hacia fuera. Qué borde toca cada
        // columna depende de la vista: con el foco a un lado, la izquierda (Contexto) va al otro borde y la
        // derecha (Trabajo) queda en medio, sin borde.
        var edges = DisplayTopology.SharedEdges(_display, DisplayEnumerator.Enumerate());
        var shared = _options.Focus switch
        {
            FocusPosition.Left => (Left: edges.Right, Right: false),
            FocusPosition.Right => (Left: edges.Left, Right: false),
            _ => (edges.Left, edges.Right),
        };
        int dpi = _display.Dpi;
        var principal = SceneLayoutEngine.Compute(new SceneInput(
            _display.WorkArea,
            new PanelInput(_assignment.ContextId, PanelSide.Left, _options.ContextWeight, _options.ContextTemplate,
                _assignment.Context.Select(w => ToLayout(w, dpi)).ToList(), shared.Left) { Title = _assignment.ContextTitle },
            _options.CenterWeight,
            _assignment.Center is null ? null : ToLayout(_assignment.Center, dpi),
            new PanelInput(_assignment.WorkId, PanelSide.Right, _options.WorkWeight, _options.WorkTemplate,
                _assignment.Work.Select(w => ToLayout(w, dpi)).ToList(), shared.Right) { Title = _assignment.WorkTitle },
            _options.Gap,
            PanelTopInset: Scale(_options.PanelTopInset, dpi))
        {
            Focus = _options.Focus,
            LeftLower = Lower(_assignment.ContextLower, PanelSide.Left, _options.ContextWeight, shared.Left),
            RightLower = Lower(_assignment.WorkLower, PanelSide.Right, _options.WorkWeight, shared.Right),
        });

        PanelInput? Lower(PanelAssignment? p, PanelSide side, double weight, bool sharedEdge) =>
            p is null
                ? null
                : new PanelInput(p.Id, side, weight, p.Template, p.Windows.Select(w => ToLayout(w, dpi)).ToList(), sharedEdge)
                {
                    Title = p.Title,
                    Slot = p.Slot,
                };

        var aux = _assignment.Aux
            .Select(a => AuxLayoutEngine.Compute(new AuxInput(
                a.Display.Key,
                a.Display.WorkArea,
                a.Panels
                    .OrderBy(p => p.Side)
                    .ThenBy(p => p.Slot)
                    .Select(p => new PanelInput(p.Id, p.Side, 1, p.Template, p.Windows.Select(w => ToLayout(w, a.Display.Dpi)).ToList()) { Title = p.Title, Slot = p.Slot })
                    .ToList(),
                _options.Gap,
                Scale(_options.PanelTopInset, a.Display.Dpi))))
            .ToList();

        return new ScenePlan(principal, aux)
        {
            Parked = _assignment.Parked
                .Select(w => new WindowTarget(w.Handle, Path.GetFileNameWithoutExtension(w.ProcessName), "Estacionada", TargetKind.Minimized, default))
                .ToList(),
        };
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

    /// <summary>Píxeles lógicos (96 DPI) a físicos del monitor.</summary>
    public static int Scale(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0);

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
