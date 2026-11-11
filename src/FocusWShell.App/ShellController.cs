using System.IO;
using FocusWShell.Core.Displays;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
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

/// <summary>Resultado de aplicar una escena guardada.</summary>
/// <param name="Missing">Apps de la escena que no están abiertas.</param>
/// <param name="Notes">Ajustes (p. ej. un monitor auxiliar que no está conectado).</param>
public sealed record SceneResult(string Name, ControllerResult Result, IReadOnlyList<SceneWindow> Missing, IReadOnlyList<string> Notes)
{
    /// <summary>Apps que faltaban y se están abriendo; al terminar llega <c>SceneAppsOpened</c>.</summary>
    public IReadOnlyList<string> Opening { get; init; } = [];

    public string Summary
    {
        get
        {
            var lines = new List<string> { $"Escena «{Name}» aplicada." };
            if (Opening.Count > 0)
            {
                lines.Add($"Abriendo las que faltaban: {string.Join(", ", Opening)}…");
            }

            var notOpening = Missing.Where(m => !Opening.Contains(m.Label)).ToList();
            if (notOpening.Count > 0)
            {
                lines.Add($"No están abiertas: {string.Join(", ", notOpening.Select(m => m.Label))}.");
            }

            lines.AddRange(Notes);
            return string.Join(Environment.NewLine, lines);
        }
    }
}

/// <summary>Un cambio que hizo el usuario (u otra app) y lo que el gestor hizo con él.</summary>
public sealed record ExternalChangeHandled(string Label, ExternalChange Change, IReadOnlyList<Command> Commands, ControllerResult Result)
{
    public string Describe() => (Change, Commands) switch
    {
        (ExternalChange.Minimized, [Park]) => $"{Label}: la minimizaste; queda estacionada en su panel.",
        (ExternalChange.Shown, [Restore]) => $"{Label}: la abriste desde la barra de tareas; vuelve a su panel.",
        (ExternalChange.Shown, [MakeActiveInPanel]) => $"{Label}: la abriste; pasa a ser la visible de su panel.",
        (ExternalChange.Maximized, [SetPresentation, MakeActiveInPanel]) => $"{Label}: la maximizaste en el monitor auxiliar; su panel pasa a Fill.",
        (ExternalChange.Unmaximized, [SetPresentation]) => $"{Label}: dejó de estar maximizada; su panel vuelve a Tiles.",
        (ExternalChange.Closed, _) => $"{Label}: se cerró; deja de gestionarse.",
        (ExternalChange.MovedOrResized, [Detach]) => $"{Label}: se puso en pantalla completa; queda fuera de layout y vuelve a su sitio al salir.",
        (_, [Detach]) => $"{Label}: la maximizaste; queda fuera de layout hasta que la restaures o la reencajes.",
        (_, [SnapBack]) => $"{Label}: la moviste; vuelve a su sitio.",
        (_, [ExitFullscreen]) => $"{Label}: dejó de estar maximizada; sale de pantalla completa y vuelve al foco.",
        (_, [SplitColumn s, SnapBack]) => $"{Label}: moviste el separador entre los dos paneles de su columna; quedan {Math.Round(100 * s.UpperHeight / (s.UpperHeight + s.LowerHeight))} % arriba y {Math.Round(100 * s.LowerHeight / (s.UpperHeight + s.LowerHeight))} % abajo.",
        (_, [SetProportions p, SnapBack]) => $"{Label}: moviste el separador; esta vista queda en {p.Weights!.Describe(p.View)}.",
        (_, [Promote]) => $"{Label}: la soltaste sobre el foco; pasa al foco.",
        (_, [SendTo s]) => $"{Label}: la soltaste sobre otro panel; se queda en él.",
        (_, [ReturnToPanel, SendTo]) => $"{Label}: la sacaste del foco a otro panel.",
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
    private EligibilityFilter? _filterCache;

    /// <summary>Lista de exclusión del usuario (M2), aplicada encima de las reglas de elegibilidad.</summary>
    private IReadOnlyList<ExclusionRule> _exclusions = [];

    private EligibilityFilter Filter => _filterCache ??= new(userExcluded: w => ExclusionRule.AnyMatches(_exclusions, w));

    /// <summary>Las reglas de exclusión actuales.</summary>
    public IReadOnlyList<ExclusionRule> Exclusions => _exclusions;

    /// <summary>
    /// Cambia la lista de exclusión (M2). Las ventanas gestionadas que pasen a estar excluidas se sueltan:
    /// quedan libres y FocusWShell deja de verlas.
    /// </summary>
    public ControllerResult SetExclusions(IEnumerable<ExclusionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        lock (_gate)
        {
            _exclusions = [.. rules];
            var released = _workspace.Windows
                .Where(m => _snapshots.TryGetValue(m.Handle, out var w) && ExclusionRule.AnyMatches(_exclusions, w))
                .Select(m => (Command)new Release(m.Handle))
                .ToList();
            return released.Count > 0 ? RunLocked(released) : new ControllerResult([], [], null);
        }
    }
    private readonly Workspace _workspace = new();
    private readonly ExpectationRegistry _expectations = new();
    private WindowEventWatcher? _watcher;

    /// <summary>Se dispara (en un hilo de fondo) cada vez que el gestor reacciona a un cambio externo.</summary>
    public event Action<ExternalChangeHandled>? ExternalChangeHandled;

    /// <summary>Se dispara (en cualquier hilo) cuando cambia lo que deben mostrar las tiras de pestañas.</summary>
    public event Action? LayoutChanged;

    /// <summary>Alto de la tira de pestañas de cada panel, en píxeles lógicos.</summary>
    public const int StripHeight = 28;

    private SceneApplyReport? _lastReport;
    private int _lastPrincipalDpi = 96;
    private Dictionary<string, int> _lastAuxDpi = [];

    /// <summary>Tiras de pestañas de los paneles, según el último layout aplicado. Ninguna en el monitor en pausa.</summary>
    public IReadOnlyList<PanelStrip> Strips()
    {
        lock (_gate)
        {
            return _lastReport is null || _workspace.Windows.Count == 0
                ? []
                : PanelStripBuilder.Build(_workspace, _lastReport, _lastPrincipalDpi, _lastAuxDpi).Where(s => !IsPaused(s.Band)).ToList();
        }
    }

    private AppFullscreenWatcher? _appFullscreen;

    /// <summary>
    /// Monitor ocupado por una app en pantalla completa propia (M2): F11, un vídeo, un juego. Mientras dure,
    /// el gestor no pone tiras, listas ni el deck encima.
    /// </summary>
    public PixelRect? PausedMonitor { get; private set; }

    /// <summary>Se dispara (en cualquier hilo) al empezar o terminar una pantalla completa propia de una app.</summary>
    public event Action<PixelRect?>? PauseChanged;

    /// <summary>Si un rectángulo (una tira, una lista, el deck) cae en el monitor en pausa.</summary>
    public bool IsPaused(PixelRect rect) =>
        PausedMonitor is { } paused && paused.Contains(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));

    private void OnAppFullscreen(PixelRect? monitor)
    {
        PausedMonitor = monitor;
        PauseChanged?.Invoke(monitor);
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Listas tipo Explorador (ADR 0015) de los paneles en lista, según el último layout: sus ventanas y
    /// las apps cerradas del catálogo cuyo último panel es ése. No hay lista en el monitor de la pantalla completa.
    /// </summary>
    public IReadOnlyList<ListPanelView> ListPanels()
    {
        lock (_gate)
        {
            if (_lastReport is null)
            {
                return [];
            }

            var fullscreen = _workspace.Composition.Fullscreen;
            var openKeys = _snapshots.Values.Select(MinSizeCatalog.KeyFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var views = new List<ListPanelView>();
            void Add(string? display, IReadOnlyDictionary<string, PixelRect> lists, int dpi)
            {
                if (fullscreen is not null && string.Equals(fullscreen.Display, display, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                foreach (var (panelId, area) in lists)
                {
                    if (_workspace.FindPanel(panelId) is not { } panel || IsPaused(area))
                    {
                        continue;
                    }

                    var items = new List<ListItem>();
                    foreach (var w in _workspace.Windows.Where(w => _workspace.EffectivePanel(w).Id == panelId && _snapshots.ContainsKey(w.Handle)))
                    {
                        var snapshot = _snapshots[w.Handle];
                        var app = _catalog.Find(MinSizeCatalog.KeyFor(snapshot));
                        var state = w switch
                        {
                            { Placement: Placement.Center or Placement.Fullscreen } => ListItemState.InFocus,
                            { Placement: Placement.Parked } => ListItemState.Parked,
                            { Detached: true } => ListItemState.Detached,
                            _ => ListItemState.InPanel,
                        };
                        items.Add(new ListItem(app?.Name ?? w.Label, MinSizeCatalog.KeyFor(snapshot), w.Handle, state, snapshot.Title, app));
                    }

                    // Las cerradas que "viven" en este panel: tocarlas las abre aquí.
                    foreach (var app in _catalog.Apps.Where(a => a.LastPanelId == panelId && !a.Hidden && !openKeys.Contains(a.AppKey)))
                    {
                        items.Add(new ListItem(app.Name, app.AppKey, null, ListItemState.Closed, "", app));
                    }

                    views.Add(new ListPanelView(panelId, panel.Name, area, dpi, panel.ListLayout, items));
                }
            }

            Add(null, _lastReport.Layout.Principal.Lists, _lastPrincipalDpi);
            foreach (var aux in _lastReport.Layout.Aux)
            {
                Add(aux.Display, aux.Lists, _lastAuxDpi.GetValueOrDefault(aux.Display, 96));
            }

            return views;
        }
    }

    /// <summary>
    /// Estado del deck (ADR 0013): el foco, la vista, cada panel con sus apps abiertas y las cerradas del
    /// catálogo cuyo último panel es ése, las ventanas libres, las escenas y los monitores. Usa la última
    /// enumeración de ventanas; quien lo muestra llama antes a <see cref="Refresh"/> si quiere lo último.
    /// </summary>
    public DeckState DeckState()
    {
        var scenes = SceneNames();
        var aux = AuxiliaryDisplays();
        lock (_gate)
        {
            string NameOf(WindowSnapshot s, string fallback) => _catalog.Find(MinSizeCatalog.KeyFor(s))?.Name ?? fallback;
            DeckApp Managed(ManagedWindow w, WindowSnapshot s) => new(
                NameOf(s, w.Label),
                MinSizeCatalog.KeyFor(s),
                w.Handle,
                w switch
                {
                    { Placement: Placement.Center or Placement.Fullscreen } => ListItemState.InFocus,
                    { Placement: Placement.Parked } => ListItemState.Parked,
                    { Detached: true } => ListItemState.Detached,
                    _ => ListItemState.InPanel,
                },
                s.Title,
                w.PanelId,
                _catalog.Find(MinSizeCatalog.KeyFor(s)));

            var openKeys = _snapshots.Values.Select(MinSizeCatalog.KeyFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string Where(Panel p) => p.Display is null
                ? $"principal, {(p.Side == PanelSide.Left ? "izquierda" : "derecha")}{(p.Slot == PanelSlot.Lower ? " abajo" : "")}"
                : $"{aux.FirstOrDefault(d => d.Key == p.Display)?.FriendlyName ?? "monitor desconectado"}, {(p.Side == PanelSide.Left ? "izquierda" : "derecha")}";

            var panels = _workspace.Panels.Select(p =>
            {
                var apps = _workspace.Windows
                    .Where(w => _workspace.EffectivePanel(w).Id == p.Id && _snapshots.ContainsKey(w.Handle))
                    .Select(w => Managed(w, _snapshots[w.Handle]))
                    .ToList();
                apps.AddRange(_catalog.Apps
                    .Where(a => a.LastPanelId == p.Id && !a.Hidden && !openKeys.Contains(a.AppKey))
                    .Select(a => new DeckApp(a.Name, a.AppKey, null, ListItemState.Closed, "", p.Id, a)));
                return new DeckPanel(p.Id, p.Name, Where(p), apps) { Display = p.Display, Template = p.Template };
            }).ToList();

            var free = _snapshots.Values
                .Where(s => !_workspace.IsManaged(s.Handle))
                .Select(s => new DeckApp(NameOf(s, Path.GetFileNameWithoutExtension(s.ProcessName)), MinSizeCatalog.KeyFor(s), s.Handle, ListItemState.InPanel, s.Title, null, _catalog.Find(MinSizeCatalog.KeyFor(s))))
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var focused = _workspace.Focused is { } f && _snapshots.TryGetValue(f.Handle, out var fs) ? Managed(f, fs) : null;
            var current = focused?.Handle;
            bool hasPrevious = _workspace.CenterHistory.Any(h => h != current && _workspace.IsManaged(h));
            var displays = new List<(string?, string)> { (null, "Principal") };
            displays.AddRange(aux.Select(d => ((string?)d.Key, d.FriendlyName ?? d.DeviceName)));
            return new DeckState(focused, hasPrevious, _workspace.View, panels, free, scenes, displays)
            {
                Weights = _workspace.ProportionsOf(_workspace.View),
            };
        }
    }

    /// <summary>La ventana en el foco (o en pantalla completa), si hay.</summary>
    public nint? FocusedWindow()
    {
        lock (_gate)
        {
            return _workspace.Focused?.Handle;
        }
    }

    /// <summary>El deck fijado en un monitor (ADR 0013, fase 4), o <c>null</c> si vive en la esquina del principal.</summary>
    public DeckDock? DeckDock { get; private set; }

    /// <summary>Fija el deck en un monitor (o lo devuelve a la esquina) y recoloca los paneles de ese monitor.</summary>
    public ControllerResult SetDeckDock(DeckDock? dock)
    {
        lock (_gate)
        {
            DeckDock = dock;
            return RunLocked([], forceLayout: true);
        }
    }

    /// <summary>El monitor con su área de trabajo reducida a lo que deja libre el deck, si está fijado en él.</summary>
    private DisplayInfo WithoutDeck(DisplayInfo display) =>
        DeckDock is { } dock && string.Equals(dock.Display, display.Key, StringComparison.OrdinalIgnoreCase)
            && Core.Layout.DeckDock.Split(display.WorkArea, dock.Zone).Panels is { } rest
            ? display with { WorkArea = rest }
            : display;

    /// <summary>Panel por defecto para una ventana que va al foco sin tener panel: el derecho del principal.</summary>
    public string DefaultPanelId()
    {
        lock (_gate)
        {
            return _workspace.Panels.FirstOrDefault(p => p.Display is null && p.Side == PanelSide.Right)?.Id
                ?? _workspace.Panels.First(p => p.Display is null).Id;
        }
    }

    /// <summary>Empieza a observar las ventanas gestionadas.</summary>
    public void StartWatching()
    {
        _watcher ??= WindowEventWatcher.Start(
            _expectations.IsWatched,
            observed => Task.Run(() => OnSettled(observed.Handle)));
        _switches ??= FocusSwitchWatcher.Start((hwnd, gesture) => Task.Run(() => OnFocusSwitch(hwnd, gesture)));
        _appFullscreen ??= AppFullscreenWatcher.Start(OnAppFullscreen);
    }

    public void Dispose()
    {
        _switches?.Dispose();
        _appFullscreen?.Dispose();
        _watcher?.Dispose();
    }

    private FocusSwitchWatcher? _switches;

    /// <summary>Qué gestos de cambio de ventana llevan al foco (se leen de la configuración).</summary>
    public Func<SwitchGesture, bool> FocusGestureEnabled { get; set; } = g => g is SwitchGesture.AltTab or SwitchGesture.TaskView or SwitchGesture.Taskbar;

    /// <summary>Se dispara (en cualquier hilo) cada vez que otra ventana pasa al primer plano.</summary>
    public event Action<nint>? ForegroundChanged;

    /// <summary>Se dispara cuando un gesto de Windows llevó una ventana al foco.</summary>
    public event Action<string, ControllerResult>? FocusGestureHandled;

    /// <summary>
    /// Una ventana llegó al primer plano por un gesto del usuario. Si el gesto está activado y la ventana
    /// es de un panel, va al foco (ADR 0003, enmienda). Los cambios que no vienen de un gesto
    /// (una app que se activa sola, un diálogo) nunca mueven nada (CA7).
    /// </summary>
    private void OnFocusSwitch(nint hwnd, SwitchGesture gesture)
    {
        ForegroundChanged?.Invoke(hwnd);

        if (gesture == SwitchGesture.Other || !FocusGestureEnabled(gesture))
        {
            return;
        }

        string message;
        ControllerResult result;
        lock (_gate)
        {
            if (_workspace.Find(hwnd) is not { Placement: Placement.InPanel or Placement.Parked, Detached: false } w)
            {
                return;
            }

            result = RunLocked([new Promote(hwnd)]);
            message = gesture switch
            {
                SwitchGesture.AltTab => $"{w.Label}: elegida con Alt+Tab; pasa al foco.",
                SwitchGesture.TaskView => $"{w.Label}: elegida en la vista de tareas; pasa al foco.",
                SwitchGesture.Taskbar => $"{w.Label}: elegida en la barra de tareas; pasa al foco.",
                _ => $"{w.Label}: clic; pasa al foco.",
            };
        }

        FocusGestureHandled?.Invoke(message, result);
    }

    /// <summary>
    /// Si el cambio de tamaño de una ventana de un panel apilado es mover el separador con el otro panel
    /// de su columna, el comando que guarda el reparto nuevo.
    /// </summary>
    private SplitColumn? StackSplit(ManagedWindow w, PixelRect before, PixelRect after, SceneApplyReport report)
    {
        var panel = _workspace.EffectivePanel(w);
        var other = _workspace.Panels.FirstOrDefault(p => p.Id != panel.Id && p.SharesColumnWith(panel));
        if (other is null)
        {
            return null;
        }

        var (bands, strip) = panel.Display is { } display && report.Layout.Aux.FirstOrDefault(a => string.Equals(a.Display, display, StringComparison.OrdinalIgnoreCase)) is { } aux
            ? (aux.Bands, aux.StripHeight)
            : (report.Layout.Principal.Bands, report.Layout.Principal.StripHeight);
        if (!bands.TryGetValue(panel.Id, out var own) || !bands.TryGetValue(other.Id, out var theirs))
        {
            return null; // el otro panel está vacío: no hay separador
        }

        var (upperPanel, upperBand, lowerPanel, lowerBand) = own.Top < theirs.Top ? (panel, own, other, theirs) : (other, theirs, panel, own);
        return StackSeparator.Detect(before, after, upperBand, lowerBand, strip) is { } heights
            ? new SplitColumn(upperPanel.Id, heights.Upper, lowerPanel.Id, heights.Lower)
            : null;
    }

    /// <summary>
    /// Dónde se soltó una ventana arrastrada, según el último layout: en la columna del foco, en la de
    /// otro panel o en ninguna.
    /// </summary>
    private (bool Focus, string? PanelId) DropTarget(ObservedWindow observed)
    {
        if (_lastReport is null || observed.Minimized)
        {
            return (false, null);
        }

        int x = observed.Visible.Left + (observed.Visible.Width / 2);
        int y = observed.Visible.Top + (observed.Visible.Height / 2);
        var principal = _lastReport.Layout.Principal;
        if (principal.CenterColumn.Contains(x, y))
        {
            return (true, null);
        }

        // Cada panel con ventanas tiene su banda; con dos en una columna, cada uno la suya.
        foreach (var (panelId, band) in principal.Bands.Concat(_lastReport.Layout.Aux.SelectMany(a => a.Bands)))
        {
            if (band.Contains(x, y))
            {
                return (false, panelId);
            }
        }

        // Una columna del principal sin ventanas no tiene banda: soltarla ahí la lleva a su panel de arriba.
        var composition = _workspace.Composition;
        if (principal.LeftColumn.Contains(x, y) && composition.Left is { } left)
        {
            return (false, left.Panel.Id);
        }

        if (principal.RightColumn.Contains(x, y) && composition.Right is { } right)
        {
            return (false, right.Panel.Id);
        }

        return (false, null);
    }

    /// <summary>
    /// Una ventana vigilada se calmó. Dentro del lock se vuelve a leer su estado: si entretanto el
    /// gestor la movió, la observación del observador ya no vale.
    /// </summary>
    private void OnSettled(nint handle)
    {
        ExternalChangeHandled? handled = null;
        lock (_gate)
        {
            var expected = _expectations.Get(handle);
            var observed = WindowMover.Observe(handle);
            var change = _expectations.Reconcile(observed);
            if (change == ExternalChange.None)
            {
                return;
            }

            var label = _workspace.Find(handle)?.Label ?? "?";
            var commands = _workspace.Interpret(handle, change);

            // Una ventana cerrada deja de estar abierta ya, sin esperar al próximo refresco: así la lista
            // de su panel la muestra enseguida como app cerrada (ADR 0015).
            if (change == ExternalChange.Closed && _snapshots.Remove(handle))
            {
                _openApps = _snapshots.Values.Select(MinSizeCatalog.KeyFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Se puso en pantalla completa ella misma (F11, un vídeo): no se le quita. Queda fuera de layout
            // y, al salir, el siguiente cambio la devuelve a su sitio (M2).
            if (commands is [SnapBack] && change == ExternalChange.MovedOrResized
                && AppFullscreen.Covers(observed.Visible, WindowEnumerator.MonitorBoundsOf(handle), observed.Maximized))
            {
                commands = [new Detach(handle)];
            }

            // Redimensionada sólo por el borde que da al otro panel de su columna: es mover el separador
            // entre los dos paneles apilados (ADR 0011). El reparto se guarda en los paneles.
            if (commands is [SnapBack] && change == ExternalChange.MovedOrResized && expected is ExpectVisible { Rect: var stackBefore }
                && _lastReport is { } stackReport && _workspace.Find(handle) is { Placement: Placement.InPanel } stacked
                && StackSplit(stacked, stackBefore, observed.Visible, stackReport) is { } split)
            {
                commands = [split, new SnapBack(handle)];
            }

            // Redimensionada sólo por el borde que comparte con la columna vecina, en el principal: es
            // mover el separador (ADR 0012). Las proporciones nuevas se guardan para esta vista.
            if (commands is [SnapBack] && change == ExternalChange.MovedOrResized && expected is ExpectVisible { Rect: var before }
                && _lastReport is { } report && _workspace.Find(handle) is { Placement: Placement.Center or Placement.InPanel } moved
                && (moved.Placement == Placement.Center || _workspace.EffectivePanel(moved).Display is null)
                && ColumnSeparator.Detect(before, observed.Visible, report.Layout.Principal) is { } weights)
            {
                commands = [new SetProportions(_workspace.View, weights), new SnapBack(handle)];
            }

            // Arrastrada y soltada: sobre el foco va al foco; sobre otro panel, a ese panel; si no, vuelve.
            if (commands is [SnapBack] && change == ExternalChange.MovedOrResized && _workspace.Find(handle) is { Placement: not Placement.Fullscreen } dragged)
            {
                var (focus, panelId) = DropTarget(WindowMover.Observe(handle));
                if (focus && dragged.Placement != Placement.Center)
                {
                    commands = [new Promote(handle)];
                }
                else if (panelId is not null && panelId != _workspace.EffectivePanel(dragged).Id)
                {
                    commands = dragged.Placement == Placement.Center
                        ? [new ReturnToPanel(handle), new SendTo(handle, panelId)]
                        : [new SendTo(handle, panelId)];
                }
            }
            var result = commands.Count > 0 ? RunLocked(commands) : new ControllerResult([], [], null);
            handled = new ExternalChangeHandled(label, change, commands, result);
        }

        ExternalChangeHandled?.Invoke(handled);
    }

    /// <summary>Última foto de cada ventana conocida: el layout y el diario necesitan proceso y título.</summary>
    private readonly Dictionary<nint, WindowSnapshot> _snapshots = [];

    public RecoveryJournal Journal { get; } = new(RecoveryJournal.DefaultPath);

    public MinSizeStore MinSizes { get; } = new(MinSizeStore.DefaultPath);

    public SceneStore Scenes { get; } = new(SceneStore.DefaultPath);

    public PanelStore PanelFile { get; } = new(PanelStore.DefaultPath);

    // --- Catálogo de apps (ADR 0011) ---------------------------------------------------------------

    public AppCatalogStore CatalogFile { get; } = new(AppCatalogStore.DefaultPath);

    private AppCatalog _catalog = new();

    /// <summary>Apps con alguna ventana abierta en el último refresco.</summary>
    private HashSet<string> _openApps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Se dispara cuando el catálogo cambia (app nueva, panel recordado, olvidada).</summary>
    public event Action? CatalogChanged;

    public void LoadCatalog()
    {
        var loaded = CatalogFile.Load();
        lock (_gate)
        {
            _catalog = loaded;
        }
    }

    /// <summary>Las apps del catálogo (sin las ocultas, salvo que se pidan) y si están abiertas.</summary>
    public IReadOnlyList<(CatalogApp App, bool Open)> CatalogApps(bool includeHidden = false)
    {
        lock (_gate)
        {
            return [.. _catalog.Apps.Where(a => includeHidden || !a.Hidden).Select(a => (a, _openApps.Contains(a.AppKey)))];
        }
    }

    /// <summary>Cambia el nombre con el que se muestra una app (y con el que se gestionan sus ventanas nuevas).</summary>
    public void RenameApp(string appKey, string name)
    {
        lock (_gate)
        {
            if (!_catalog.Rename(appKey, name))
            {
                return;
            }

            CatalogFile.Save(_catalog);
        }

        CatalogChanged?.Invoke();
    }

    /// <summary>Oculta una app del catálogo (sigue registrada: no vuelve a aparecer como nueva) o la muestra.</summary>
    public void SetAppHidden(string appKey, bool hidden)
    {
        lock (_gate)
        {
            if (!_catalog.SetHidden(appKey, hidden))
            {
                return;
            }

            CatalogFile.Save(_catalog);
        }

        CatalogChanged?.Invoke();
    }

    public void ForgetApp(string appKey)
    {
        lock (_gate)
        {
            if (_catalog.Forget(appKey))
            {
                CatalogFile.Save(_catalog);
            }
        }

        CatalogChanged?.Invoke();
    }

    /// <summary>Nombre automático de cada app en esta sesión (leerlo cuesta: versión del ejecutable o el shell).</summary>
    private readonly Dictionary<string, string> _autoNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Nombre de la app de una ventana: el del catálogo o, si no está, el del proceso. Así las apps de la
    /// Store dejan de verse como "ApplicationFrameHost".
    /// </summary>
    public string AppName(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_gate)
        {
            return _catalog.Find(MinSizeCatalog.KeyFor(window))?.Name ?? Path.GetFileNameWithoutExtension(window.ProcessName);
        }
    }

    /// <summary>Registra las apps que se ven. Sólo escribe el archivo si hay algo nuevo.</summary>
    private void ObserveApps(IReadOnlyList<WindowSnapshot> windows)
    {
        bool changed = false;
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in windows)
        {
            var key = MinSizeCatalog.KeyFor(w);
            open.Add(key);
            var known = _catalog.Find(key);
            bool firstTime = !_autoNames.TryGetValue(key, out var auto);
            if (firstTime)
            {
                auto = AppLauncher.FriendlyName(w.ProcessPath, w.AppUserModelId) ?? Path.GetFileNameWithoutExtension(w.ProcessName);
                _autoNames[key] = auto;
            }

            changed |= _catalog.Observe(key, known?.Name ?? auto!, w.ProcessPath, w.AppUserModelId, DateTimeOffset.Now);

            // Corrige nombres automáticos de criterios anteriores ("Electron", "Notepad.exe" o, en las apps
            // de la Store, "Application Frame Host"), salvo que el usuario la haya renombrado.
            if (firstTime && known is { Renamed: false } && known.Name != auto
                && (known.Name.Equals("Electron", StringComparison.OrdinalIgnoreCase)
                    || known.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || known.Name == AppLauncher.FriendlyName(w.ProcessPath)))
            {
                changed |= _catalog.ImproveName(key, known.Name, auto!);
            }
        }

        bool openChanged = !open.SetEquals(_openApps);
        _openApps = open;
        if (changed)
        {
            CatalogFile.Save(_catalog);
        }

        if (changed || openChanged)
        {
            CatalogChanged?.Invoke();
        }
    }

    /// <summary>Recuerda el panel de cada app gestionada, para sugerirlo al abrirla otra vez.</summary>
    private void RememberAppPanels()
    {
        bool changed = false;
        foreach (var w in _workspace.Windows)
        {
            if (_snapshots.TryGetValue(w.Handle, out var snapshot))
            {
                changed |= _catalog.RememberPanel(MinSizeCatalog.KeyFor(snapshot), w.PanelId);
            }
        }

        if (changed)
        {
            CatalogFile.Save(_catalog);
            CatalogChanged?.Invoke();
        }
    }

    /// <summary>
    /// Abre una app del catálogo y asigna su ventana al panel. Espera hasta 20 s a que aparezca;
    /// prefiere una ventana nueva y, pasados unos segundos, acepta una que ya estuviera abierta y libre.
    /// Devuelve un mensaje para el usuario.
    /// </summary>
    public string OpenApp(string appKey, string panelId)
    {
        CatalogApp? app;
        HashSet<nint> before;
        string panelName;
        lock (_gate)
        {
            app = _catalog.Find(appKey);
            before = [.. _snapshots.Keys];
            panelName = _workspace.FindPanel(panelId)?.Name ?? panelId;
        }

        if (app is null)
        {
            return "Esa app ya no está en el catálogo.";
        }

        if (AppLauncher.Launch(app) is { } error)
        {
            return error;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            Thread.Sleep(400);
            var windows = Refresh();
            lock (_gate)
            {
                var free = windows.Where(w => !_workspace.IsManaged(w.Handle)
                    && string.Equals(MinSizeCatalog.KeyFor(w), appKey, StringComparison.OrdinalIgnoreCase)).ToList();
                var pick = free.FirstOrDefault(w => !before.Contains(w.Handle))
                    ?? (clock.Elapsed > TimeSpan.FromSeconds(3) ? free.FirstOrDefault() : null);
                if (pick is null)
                {
                    continue;
                }

                var result = RunLocked([new Adopt(pick.Handle, app.Name, panelId)]);
                return result.Rejections.Count == 0
                    ? $"{app.Name} abierta en {panelName}."
                    : string.Join(" ", result.Rejections);
            }
        }

        return $"{app.Name} se abrió, pero no apareció ninguna ventana suya en 20 s; queda libre.";
    }

    /// <summary>Lo último que se guardó en panels.json, para escribir sólo cuando cambia.</summary>
    private List<Panel> _savedPanels = [];

    /// <summary>Carga los paneles guardados (si los hay) en el modelo. Se llama al arrancar.</summary>
    public void LoadPanels()
    {
        var (saved, view, proportions) = PanelFile.LoadAll();
        lock (_gate)
        {
            if (saved.Count > 0)
            {
                _workspace.Execute(new ReplacePanels(saved));
            }

            _workspace.Execute(new SetView(view));
            foreach (var (v, weights) in proportions)
            {
                _workspace.Execute(new SetProportions(v, weights));
            }

            _savedPanels = [.. _workspace.Panels];
            _savedView = _workspace.View;
            _savedProportions = new(_workspace.CustomProportions);
        }
    }

    private FocusPosition _savedView;
    private Dictionary<FocusPosition, ViewWeights> _savedProportions = [];

    /// <summary>Proporciones de la vista actual.</summary>
    public ViewWeights Proportions
    {
        get
        {
            lock (_gate)
            {
                return _workspace.ProportionsOf(_workspace.View);
            }
        }
    }

    /// <summary>Monitor de la pantalla completa (<c>null</c>: el principal o ninguna).</summary>
    public string? FullscreenDisplay
    {
        get
        {
            lock (_gate)
            {
                return _workspace.Composition.Fullscreen?.Display;
            }
        }
    }

    /// <summary>Vista actual del principal (ADR 0012).</summary>
    public FocusPosition View
    {
        get
        {
            lock (_gate)
            {
                return _workspace.View;
            }
        }
    }

    /// <summary>Guarda los paneles si cambiaron (nombre, ubicación, presentación, altas o bajas).</summary>
    private void SavePanelsIfChanged()
    {
        bool proportionsChanged = _workspace.CustomProportions.Count != _savedProportions.Count
            || _workspace.CustomProportions.Any(p => !_savedProportions.TryGetValue(p.Key, out var w) || w != p.Value);
        if (!_workspace.Panels.SequenceEqual(_savedPanels) || _workspace.View != _savedView || proportionsChanged)
        {
            _savedPanels = [.. _workspace.Panels];
            _savedView = _workspace.View;
            _savedProportions = new(_workspace.CustomProportions);
            PanelFile.Save(_savedPanels, _savedView, _savedProportions);
        }
    }

    /// <summary>Monitores auxiliares de izquierda a derecha: el 1 es el primero. Así se guardan en las escenas.</summary>
    private static List<DisplayInfo> OrderedAux() =>
        [.. AuxiliaryDisplays().OrderBy(d => d.Bounds.Left).ThenBy(d => d.Bounds.Top)];

    /// <summary>Se dispara al guardar o borrar una escena.</summary>
    public event Action? ScenesChanged;

    public IReadOnlyList<string> SceneNames() => Scenes.Load().Select(s => s.Name).ToList();

    public void DeleteScene(string name)
    {
        Scenes.Delete(name);
        ScenesChanged?.Invoke();
    }

    /// <summary>Guarda (o reemplaza) la escena actual con ese nombre.</summary>
    public SavedScene SaveScene(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var scene = CaptureScene(name);
        Scenes.Upsert(scene);
        ScenesChanged?.Invoke();
        return scene;
    }

    // --- Sesión: qué app estaba en qué panel, para recuperarlo al volver a abrir FocusWShell -------------

    private const string SessionName = "Última sesión";

    /// <summary>La última sesión: <c>%LOCALAPPDATA%\FocusWShell\session.json</c>, una escena que no sale en la lista.</summary>
    public SceneStore Session { get; } = new(Path.Combine(Path.GetDirectoryName(SceneStore.DefaultPath)!, "session.json"));

    /// <summary>Ventanas de la sesión que aún no han aparecido (la app tarda en abrirse al iniciar Windows).</summary>
    private List<SceneWindow> _sessionPending = [];

    /// <summary>Guarda qué ventana está en qué panel (se llama tras los cambios y al salir).</summary>
    public void SaveSession() => Session.Save([CaptureScene(SessionName)]);

    /// <summary>
    /// Al arrancar: vuelve a poner en sus paneles las ventanas de la última sesión que estén abiertas, con
    /// la vista de entonces. No toca los paneles (ya se guardan aparte) ni abre apps. Las que falten se
    /// adoptan si aparecen en los próximos minutos (<see cref="AdoptLateSessionWindows"/>).
    /// </summary>
    public ControllerResult? RestoreSession()
    {
        if (Session.Load().FirstOrDefault()?.Upgrade() is not { Windows.Count: > 0 } scene)
        {
            return null;
        }

        var open = Refresh().Select(w => new OpenWindow(w.Handle, MinSizeCatalog.KeyFor(w), w.Title)).ToList();
        var match = SceneMatcher.Match(scene, open, strict: true);
        lock (_gate)
        {
            _sessionPending = [.. match.Missing];
        }

        if (match.Matched.Count == 0)
        {
            return null;
        }

        var entries = match.Matched
            .Select(m => new SceneEntry(m.Window.Handle, m.Entry.Label, m.Entry.PanelId ?? Workspace.WorkPanel, m.Entry.Placement) { Size = m.Entry.Size })
            .ToList();
        return Execute(new ApplyScene(entries, []) { View = scene.View, Weights = scene.Weights });
    }

    /// <summary>
    /// Adopta las ventanas de la última sesión que aparecen tarde (p. ej. apps que se abren al iniciar
    /// Windows después que FocusWShell). Sólo ventanas libres: no mueve nada de lo que el usuario ya puso.
    /// </summary>
    public ControllerResult? AdoptLateSessionWindows()
    {
        List<SceneWindow> pending;
        lock (_gate)
        {
            pending = [.. _sessionPending];
        }

        if (pending.Count == 0)
        {
            return null;
        }

        var windows = Refresh();
        lock (_gate)
        {
            var free = windows.Where(w => !_workspace.IsManaged(w.Handle))
                .Select(w => new OpenWindow(w.Handle, MinSizeCatalog.KeyFor(w), w.Title))
                .ToList();
            var match = SceneMatcher.Match(new SavedScene(SessionName, pending), free, strict: true);
            if (match.Matched.Count == 0)
            {
                return null;
            }

            _sessionPending = [.. match.Missing];
            var commands = new List<Command>();
            foreach (var (entry, window) in match.Matched)
            {
                var panel = _workspace.FindPanel(entry.PanelId ?? Workspace.WorkPanel);
                if (panel is null)
                {
                    continue; // su panel ya no existe: queda libre
                }

                commands.Add(new Adopt(window.Handle, entry.Label, panel.Id));
                if (entry.Placement == ScenePlacement.Parked)
                {
                    commands.Add(new Park(window.Handle));
                }
                else if (entry.Placement == ScenePlacement.Center && _workspace.Focused is null)
                {
                    commands.Add(new Promote(window.Handle));
                }
            }

            return commands.Count > 0 ? RunLocked(commands) : null;
        }
    }

    /// <summary>La escena actual (ventanas gestionadas, paneles, vista), sin guardarla.</summary>
    private SavedScene CaptureScene(string name)
    {
        Refresh();
        var aux = OrderedAux();
        int? AuxIndex(string? display) => display is null ? null : aux.FindIndex(d => d.Key == display) is int i and >= 0 ? i + 1 : null;

        SavedScene scene;
        lock (_gate)
        {
            var windows = _workspace.Windows
                .Where(w => _snapshots.ContainsKey(w.Handle))
                .Select(w =>
                {
                    var snapshot = _snapshots[w.Handle];
                    var placement = w.Placement switch
                    {
                        Placement.Center => ScenePlacement.Center,
                        Placement.Parked => ScenePlacement.Parked,
                        _ => ScenePlacement.InPanel,
                    };
                    return new SceneWindow(MinSizeCatalog.KeyFor(snapshot), w.Label, snapshot.Title, w.PanelId, placement) { Size = w.Size };
                })
                .ToList();
            var panels = _workspace.Panels
                .Select(p => new ScenePanel(p.Id, p.Name, AuxIndex(p.Display), p.Side, p.Template, p.Slot, p.Height))
                .ToList();
            scene = new SavedScene(name.Trim(), windows)
            {
                Panels = panels,
                View = _workspace.View,
                Weights = _workspace.ProportionsOf(_workspace.View),
            };
        }

        return scene;
    }

    /// <summary>
    /// Aplica una escena guardada: empareja sus ventanas con las abiertas y la monta de una vez.
    /// Las ventanas gestionadas que no son de la escena vuelven a su sitio original.
    /// </summary>
    public SceneResult ApplySavedScene(string name)
    {
        var scene = Scenes.Load().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Upgrade();
        if (scene is null)
        {
            return new SceneResult(name, new ControllerResult([$"No existe la escena \"{name}\"."], [], null), [], []);
        }

        var open = Refresh()
            .Select(w => new OpenWindow(w.Handle, MinSizeCatalog.KeyFor(w), w.Title))
            .ToList();
        var match = SceneMatcher.Match(scene, open);
        var aux = OrderedAux();
        var notes = new List<string>();
        string? DisplayOf(int? index)
        {
            if (index is null)
            {
                return null;
            }

            if (index.Value <= aux.Count)
            {
                return aux[index.Value - 1].Key;
            }

            notes.Add($"El monitor auxiliar {index} no está conectado: sus paneles van al principal.");
            return null;
        }

        var entries = match.Matched
            .Select(m => new SceneEntry(m.Window.Handle, m.Entry.Label, m.Entry.PanelId ?? Workspace.WorkPanel, m.Entry.Placement) { Size = m.Entry.Size })
            .ToList();
        var panels = scene.Panels
            .Select(p => new Panel(p.Id, p.Name, DisplayOf(p.AuxIndex), p.Side, p.Template, p.Slot, p.Height))
            .ToList();
        var result = Execute(new ApplyScene(entries, panels) { View = scene.View, Weights = scene.Weights });

        // Las apps que faltan y se saben abrir se abren en segundo plano; cada ventana va a su panel.
        var opening = new List<(SceneWindow Entry, CatalogApp App, string? PanelId)>();
        if (OpenMissingApps() && match.Missing.Count > 0)
        {
            lock (_gate)
            {
                foreach (var entry in match.Missing.DistinctBy(m => m.AppKey, StringComparer.OrdinalIgnoreCase))
                {
                    if (_catalog.Find(entry.AppKey) is { CanLaunch: true } app)
                    {
                        var scenePanel = panels.FirstOrDefault(p => p.Id == (entry.PanelId ?? Workspace.WorkPanel));
                        var panelId = scenePanel is null
                            ? _workspace.FindPanel(entry.PanelId ?? Workspace.WorkPanel)?.Id
                            : _workspace.ResolveScenePanel(scenePanel)?.Id;
                        opening.Add((entry, app, panelId));
                    }
                }
            }
        }

        if (opening.Count > 0)
        {
            Task.Run(() => OpenSceneApps(scene.Name, opening));
        }

        return new SceneResult(scene.Name, result, match.Missing, notes.Distinct().ToList())
        {
            Opening = opening.Select(o => o.Entry.Label).ToList(),
        };
    }

    /// <summary>Si al aplicar una escena se abren las apps que faltan (configuración).</summary>
    public Func<bool> OpenMissingApps { get; set; } = () => true;

    /// <summary>Al terminar de abrir las apps que faltaban en una escena: el resumen para el usuario.</summary>
    public event Action<string, ControllerResult>? SceneAppsOpened;

    /// <summary>
    /// Abre cada app y espera (hasta 30 s en total) a que aparezca una ventana suya nueva. La pone en su
    /// panel y, si en la escena estaba en el foco o estacionada, ahí. Si su panel no se resuelve, queda libre.
    /// </summary>
    private void OpenSceneApps(string sceneName, List<(SceneWindow Entry, CatalogApp App, string? PanelId)> opening)
    {
        HashSet<nint> before;
        lock (_gate)
        {
            before = [.. _snapshots.Keys];
        }

        var lines = new List<string>();
        var commands = new List<Command>();
        var pending = new List<(SceneWindow Entry, CatalogApp App, string? PanelId)>();
        foreach (var o in opening)
        {
            if (AppLauncher.Launch(o.App) is { } error)
            {
                lines.Add(error);
            }
            else
            {
                pending.Add(o);
            }
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<ControllerResult>();
        while (pending.Count > 0 && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(500);
            var windows = Refresh();
            lock (_gate)
            {
                foreach (var o in pending.ToList())
                {
                    var window = windows.FirstOrDefault(w => !before.Contains(w.Handle) && !_workspace.IsManaged(w.Handle)
                        && string.Equals(MinSizeCatalog.KeyFor(w), o.Entry.AppKey, StringComparison.OrdinalIgnoreCase));
                    if (window is null)
                    {
                        continue;
                    }

                    pending.Remove(o);
                    if (o.PanelId is null)
                    {
                        lines.Add($"{o.Entry.Label} se abrió; su panel no existe, así que queda libre.");
                        continue;
                    }

                    List<Command> place = [new Adopt(window.Handle, o.Entry.Label, o.PanelId), new SetTileSize(window.Handle, o.Entry.Size)];
                    if (o.Entry.Placement == ScenePlacement.Center)
                    {
                        place.Add(new Promote(window.Handle));
                    }
                    else if (o.Entry.Placement == ScenePlacement.Parked)
                    {
                        place.Add(new Park(window.Handle));
                    }

                    results.Add(RunLocked(place));
                    lines.Add($"{o.Entry.Label} se abrió y está en {(o.Entry.Placement == ScenePlacement.Center ? "el foco" : _workspace.FindPanel(o.PanelId)?.Name ?? o.PanelId)}.");
                }
            }
        }

        lines.AddRange(pending.Select(o => $"{o.Entry.Label} se abrió, pero no apareció ninguna ventana suya en 30 s; queda libre."));
        var last = results.LastOrDefault() ?? new ControllerResult([], [], null);
        var summary = new ControllerResult([.. results.SelectMany(r => r.Rejections)], [.. results.SelectMany(r => r.Restores)], last.Applied);
        SceneAppsOpened?.Invoke($"Escena «{sceneName}»: " + string.Join(" ", lines), summary);
    }

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
        var candidates = WindowEnumerator.EnumerateTopLevel()
            .Select(w => (Window: w, Eligibility: Filter.Evaluate(w, own)))
            .Where(c => c.Eligibility.IsCandidate)
            .ToList();
        var windows = candidates.Where(c => c.Eligibility.Manageable).Select(c => c.Window).ToList();

        lock (_gate)
        {
            // Las que se ven pero no se pueden mover (M2): elevadas o del sistema. Se muestran con su motivo.
            _unmanageable = candidates
                .Where(c => !c.Eligibility.Manageable)
                .Select(c => (c.Window, Unmanageable.Reason(c.Window.Integrity, own) ?? "no se puede mover"))
                .ToList();

            foreach (var w in windows)
            {
                _snapshots[w.Handle] = w;
            }

            ObserveApps(windows);
            var alive = windows.Select(w => w.Handle).ToHashSet();
            // Sólo se da por cerrada la ventana que ya no existe. Una gestionada puede dejar de ser "elegible"
            // un rato sin cerrarse (p. ej. en pantalla completa propia cambia de estilo): sigue gestionada.
            var closed = _workspace.Windows
                .Where(m => !alive.Contains(m.Handle) && !WindowMover.Observe(m.Handle).Exists)
                .Select(m => new WindowClosed(m.Handle))
                .ToList();
            if (closed.Count > 0)
            {
                RunLocked(closed);
            }

            // Las gestionadas que siguen vivas pero no son elegibles ahora siguen en la lista, con su última foto.
            windows.AddRange(_workspace.Windows
                .Where(m => !alive.Contains(m.Handle) && _snapshots.ContainsKey(m.Handle))
                .Select(m => _snapshots[m.Handle]));
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

    private List<(WindowSnapshot Window, string Reason)> _unmanageable = [];

    /// <summary>
    /// Ventanas que se ven pero FocusWShell no puede mover, con el motivo (M2), según el último
    /// <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<(WindowSnapshot Window, string Reason)> UnmanageableWindows()
    {
        lock (_gate)
        {
            return [.. _unmanageable];
        }
    }

    /// <summary>Promueve la ventana en primer plano, si está gestionada (atajo).</summary>
    public ControllerResult PromoteForeground()
    {
        var foreground = WindowMover.Foreground();
        lock (_gate)
        {
            if (_workspace.IsManaged(foreground))
            {
                return RunLocked([new Promote(foreground)]);
            }

            var why = _unmanageable.FirstOrDefault(u => u.Window.Handle == foreground);
            return new ControllerResult(
                [why.Window is not null
                    ? $"{why.Window.Title}: FocusWShell no puede moverla, {why.Reason}."
                    : "La ventana activa no está gestionada por FocusWShell."],
                [],
                null);
        }
    }

    /// <summary>Devuelve el Centro a su panel (atajo).</summary>
    /// <summary>
    /// Pantalla completa de la ventana del foco en el principal, o salir si ya lo está (atajo F).
    /// </summary>
    public ControllerResult ToggleFullscreen(string? display = null)
    {
        lock (_gate)
        {
            if (_workspace.Focused is not { } focused)
            {
                return new ControllerResult(["No hay ninguna ventana en el foco."], [], null);
            }

            return focused.Placement == Placement.Fullscreen && display is null
                ? RunLocked([new ExitFullscreen(focused.Handle)])
                : RunLocked([new EnterFullscreen(focused.Handle, display)]);
        }
    }

    /// <summary>Al arrancar: si hay paneles en lista, se aplica el layout para que se vean (sin ventanas, de lanzador).</summary>
    public void ShowLists()
    {
        lock (_gate)
        {
            if (_workspace.Panels.Any(p => p.Template == PanelTemplate.List))
            {
                RunLocked([], forceLayout: true);
            }
        }
    }

    public ControllerResult ReturnCenter()
    {
        lock (_gate)
        {
            return _workspace.Focused is { } c
                ? RunLocked([new ReturnToPanel(c.Handle)])
                : new ControllerResult(["El Centro está vacío."], [], null);
        }
    }

    private ControllerResult RunLocked(IReadOnlyList<Command> commands, bool forceLayout = false)
    {
        // Lo que el gestor mueva aquí no es del usuario: el observador calla hasta que haya
        // expectativas nuevas (ADR 0007).
        using var own = _watcher?.OwnChange();
        var result = RunEffectsLocked(commands, forceLayout);
        SyncExpectations(result.Applied);
        SavePanelsIfChanged();
        RememberAppPanels();
        if (result.Applied is { Error: null } applied)
        {
            _lastReport = applied;
        }

        LayoutChanged?.Invoke();
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

    private ControllerResult RunEffectsLocked(IReadOnlyList<Command> commands, bool forceLayout)
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
                case ForgetOriginal f:
                    // La ventana queda libre donde está (o ya no existe): su entrada sale del diario sin aplicarla.
                    var entries = Journal.Load();
                    if (entries.RemoveAll(e => e.Handle == f.Handle) > 0)
                    {
                        Journal.Save(entries);
                    }

                    break;
                case RestoreEverything:
                    restores.Add(JournalRestorer.Restore(Journal));
                    break;
            }
        }

        SceneApplyReport? applied = null;
        // Sin ventanas gestionadas no hay nada que colocar, salvo que haya listas: hacen de lanzador (ADR 0015).
        if ((forceLayout || effects.Any(e => e is ApplyLayout))
            && (_workspace.Windows.Count > 0 || _workspace.Panels.Any(p => p.Template == PanelTemplate.List)))
        {
            applied = ApplyLayoutLocked();

            // Las de un panel en lista van detrás de la lista. Al colocarlas, Windows puede traer alguna al
            // frente (p. ej. al restaurar una minimizada) después de que la lista se pusiera encima, y la que
            // vuelve del foco aún tiene el primer plano (desde el deck, que nunca se activa, nadie se lo
            // quita). Al fondo del orden Z no tapan nada, y la lista sigue viendo sus miniaturas.
            foreach (var w in _workspace.Windows.Where(w => w is { Placement: Placement.InPanel, Detached: false }
                && _workspace.EffectivePanel(w).Template == PanelTemplate.List))
            {
                WindowMover.SendToBack(w.Handle);
            }
        }

        foreach (var activate in effects.OfType<Activate>())
        {
            WindowMover.Activate(activate.Handle);
        }

        return new ControllerResult(rejections, restores, applied);
    }

    /// <summary>
    /// Compara los monitores que conoce el modelo con los conectados (S6): los que faltan dejan sus
    /// ventanas en el principal; los que vuelven las recuperan. Devuelve si cambió algo.
    /// </summary>
    private bool ReconcileDisplaysLocked(IReadOnlyList<DisplayInfo> displays)
    {
        foreach (var d in displays)
        {
            _displayNames[d.Key] = d.FriendlyName ?? d.DeviceName;
        }

        var plan = DisplayReconcile.Compute([.. _workspace.KnownDisplays], _workspace.IsDisplayMissing, displays, _principalKey);
        _principalKey = plan.Principal ?? _principalKey;
        bool changed = false;
        foreach (var command in plan.Commands)
        {
            changed |= _workspace.Execute(command).Effects.Count > 0;
        }

        foreach (var e in plan.Events)
        {
            _displayEvents.Add(e switch
            {
                DisplayReturned r => $"Volvió {NameOf(r.Display)}: sus ventanas regresan a él.",
                DisplayLost l => $"Se desconectó {NameOf(l.Display)}: sus ventanas pasan a los paneles del principal hasta que vuelva.",
                PrincipalChanged { PreviousConnected: false } p =>
                    $"Se desconectó el principal ({NameOf(p.Previous)}): {NameOf(p.Current)} hace de principal, con el Centro y todas las ventanas, hasta que vuelva.",
                PrincipalChanged p => $"{NameOf(p.Current)} vuelve a ser el principal; {NameOf(p.Previous)} recupera sus paneles.",
                _ => e.ToString(),
            });
        }

        return changed;
    }

    /// <summary>El monitor principal de la última reconciliación (M2: si cambia, se avisa).</summary>
    private string? _principalKey;

    private readonly Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _displayEvents = [];
    private IDisposable? _displayChange;

    private string NameOf(string key) => _displayNames.GetValueOrDefault(key, "un monitor");

    public bool IsDisplayMissing(string? display)
    {
        lock (_gate)
        {
            return _workspace.IsDisplayMissing(display);
        }
    }

    /// <summary>
    /// Windows avisó de un cambio de monitores (S6). Mientras dure, el observador calla: Windows mueve
    /// ventanas por su cuenta y no deben tomarse como movimientos del usuario.
    /// </summary>
    public void BeginDisplayChange()
    {
        lock (_gate)
        {
            _displayChange ??= _watcher?.OwnChange() ?? new NoScope();
        }
    }

    /// <summary>
    /// La configuración de monitores se estabilizó: se reconcilian los monitores, se reaplica el layout
    /// y vuelve la escucha. Devuelve qué cambió (vacío si nada que contar).
    /// </summary>
    public (IReadOnlyList<string> Events, ControllerResult? Result) EndDisplayChange()
    {
        Refresh();
        lock (_gate)
        {
            try
            {
                _displayEvents.Clear();
                ControllerResult? result = null;
                if (_workspace.Windows.Count > 0)
                {
                    // ApplyLayoutLocked reconcilia los monitores antes de calcular.
                    result = RunLocked([], forceLayout: true);
                }
                else
                {
                    ReconcileDisplaysLocked(DisplayEnumerator.Enumerate());
                }

                return ([.. _displayEvents], result);
            }
            finally
            {
                _displayChange?.Dispose();
                _displayChange = null;
            }
        }
    }

    private sealed class NoScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Las gestionadas de tamaño fijo y su tamaño propio. Sale del diario (su rectángulo antes de que
    /// FocusWShell la tocara) para corregir las que ya se estiraron.
    /// </summary>
    private Dictionary<nint, PixelSize> FixedSizesLocked()
    {
        var originals = Journal.Load().ToDictionary(e => (nint)e.Handle, e => e.NormalPosition);
        var sizes = new Dictionary<nint, PixelSize>();
        foreach (var m in _workspace.Windows)
        {
            if (_snapshots.TryGetValue(m.Handle, out var w) && FixedSize.IsFixed(w.IsResizable, SizeModeOf(w)))
            {
                sizes[m.Handle] = FixedSize.Natural(w.VisibleBounds, w.Bounds, originals.TryGetValue(m.Handle, out var o) ? o : null);
            }
        }

        return sizes;
    }

    private AppSizeMode SizeModeOf(WindowSnapshot w) => _catalog.Find(MinSizeCatalog.KeyFor(w))?.SizeMode ?? AppSizeMode.Auto;

    /// <summary>
    /// Cómo trata el layout el tamaño de una app: según la ventana, fijo o siempre redimensionable.
    /// Se guarda en el catálogo y se recoloca al momento.
    /// </summary>
    public ControllerResult SetAppSizeMode(string appKey, AppSizeMode mode)
    {
        ControllerResult result;
        lock (_gate)
        {
            if (!_catalog.SetSizeMode(appKey, mode))
            {
                return new ControllerResult([], [], null);
            }

            CatalogFile.Save(_catalog);
            result = RunLocked([], forceLayout: _workspace.Windows.Count > 0);
        }

        CatalogChanged?.Invoke();
        return result;
    }

    /// <summary>Si FocusWShell trata esta ventana como de tamaño fijo, y por qué (para los menús).</summary>
    public (bool Fixed, AppSizeMode Mode, string AppKey) SizeInfo(nint handle)
    {
        lock (_gate)
        {
            if (!_snapshots.TryGetValue(handle, out var w))
            {
                return (false, AppSizeMode.Auto, "");
            }

            var mode = SizeModeOf(w);
            return (FixedSize.IsFixed(w.IsResizable, mode), mode, MinSizeCatalog.KeyFor(w));
        }
    }

    private SceneApplyReport ApplyLayoutLocked()
    {
        var displays = DisplayEnumerator.Enumerate();
        var principal = DisplayTopology.Principal(displays) ?? throw new InvalidOperationException("No se encontró ningún monitor.");

        ReconcileDisplaysLocked(displays);

        var c = _workspace.Composition;
        List<WindowSnapshot> Map(IEnumerable<nint> handles) =>
            handles.Where(_snapshots.ContainsKey).Select(h => _snapshots[h]).ToList();
        PanelAssignment? Assign(PanelView? v) =>
            v is null ? null : new PanelAssignment(v.Panel.Id, v.Panel.Name, v.Panel.Side, v.Panel.Template, Map(v.Windows)) { Slot = v.Panel.Slot, Height = v.Panel.Height };

        var assignment = new SceneAssignment(
            Map(c.Context),
            c.Center is { } center && _snapshots.TryGetValue(center, out var w) ? w : null,
            Map(c.Work))
        {
            ContextId = c.Left?.Panel.Id ?? Workspace.ContextPanel,
            ContextTitle = c.Left?.Panel.Name ?? "Contexto",
            WorkId = c.Right?.Panel.Id ?? Workspace.WorkPanel,
            WorkTitle = c.Right?.Panel.Name ?? "Trabajo",
            ContextHeight = c.Left?.Panel.Height ?? 1,
            WorkHeight = c.Right?.Panel.Height ?? 1,
            ContextLower = Assign(c.LeftLower),
            WorkLower = Assign(c.RightLower),
            Parked = Map(c.Parked),
            TileSizes = _workspace.Windows.ToDictionary(w => w.Handle, w => w.Size),
            FixedSizes = FixedSizesLocked(),
            Scroll = new[] { c.Left, c.LeftLower, c.Right, c.RightLower }.OfType<PanelView>().Concat(c.Aux.SelectMany(a => a.Panels))
                .Where(v => v.ScrollRow > 0 || v.Reveal is not null)
                .ToDictionary(v => v.Panel.Id, v => (v.ScrollRow, v.Reveal)),
            Fullscreen = c.Fullscreen is { } full && _snapshots.TryGetValue(full.Handle, out var fullWindow)
                ? (fullWindow, full.Display is { } fd && displays.FirstOrDefault(d => d.Key == fd) is { } fullDisplay ? fullDisplay : principal)
                : null,
            Aux = c.Aux
                .Select(a => new AuxAssignment(
                    WithoutDeck(displays.First(d => d.Key == a.Display)),
                    a.Panels.Select(v => Assign(v)!).ToList())
                {
                    NoMaximize = DeckDock is { } dock && string.Equals(dock.Display, a.Display, StringComparison.OrdinalIgnoreCase),
                })
                .ToList(),
        };
        var (leftWeight, centerWeight, rightWeight) = c.Weights;
        var options = new SceneOptions
        {
            Focus = c.View,
            ContextWeight = leftWeight,
            CenterWeight = centerWeight,
            WorkWeight = rightWeight,
            ContextTemplate = c.ContextTemplate,
            WorkTemplate = c.WorkTemplate,
            PanelTopInset = StripHeight,
        };
        _lastPrincipalDpi = principal.Dpi;
        _lastAuxDpi = assignment.Aux.ToDictionary(a => a.Display.Key, a => a.Display.Dpi);
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

    /// <summary>Copia de los paneles, para la UI.</summary>
    public IReadOnlyList<Panel> Panels()
    {
        lock (_gate)
        {
            return [.. _workspace.Panels];
        }
    }

    /// <summary>Panel donde se coloca de verdad una ventana (el suyo o, si su monitor falta, el del principal).</summary>
    public Panel EffectivePanel(ManagedWindow window)
    {
        lock (_gate)
        {
            return _workspace.EffectivePanel(window);
        }
    }
}
