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

/// <summary>Resultado de aplicar una escena guardada.</summary>
/// <param name="Missing">Apps de la escena que no están abiertas.</param>
/// <param name="Notes">Ajustes (p. ej. un monitor auxiliar que no está conectado).</param>
public sealed record SceneResult(string Name, ControllerResult Result, IReadOnlyList<SceneWindow> Missing, IReadOnlyList<string> Notes)
{
    public string Summary
    {
        get
        {
            var lines = new List<string> { $"Escena «{Name}» aplicada." };
            if (Missing.Count > 0)
            {
                lines.Add($"No están abiertas: {string.Join(", ", Missing.Select(m => m.Label))}.");
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
        (ExternalChange.Maximized, [MakeActiveInPanel, SetPresentation]) => $"{Label}: la maximizaste en el monitor auxiliar; su panel pasa a Fill.",
        (ExternalChange.Unmaximized, [SetPresentation]) => $"{Label}: dejó de estar maximizada; su panel vuelve a Tiles.",
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

    /// <summary>Se dispara (en cualquier hilo) cuando cambia lo que deben mostrar las tiras de pestañas.</summary>
    public event Action? LayoutChanged;

    /// <summary>Alto de la tira de pestañas de cada panel, en píxeles lógicos.</summary>
    public const int StripHeight = 28;

    private SceneApplyReport? _lastReport;
    private int _lastPrincipalDpi = 96;
    private Dictionary<string, int> _lastAuxDpi = [];

    /// <summary>Tiras de pestañas de los paneles, según el último layout aplicado.</summary>
    public IReadOnlyList<PanelStrip> Strips()
    {
        lock (_gate)
        {
            return _lastReport is null || _workspace.Windows.Count == 0
                ? []
                : PanelStripBuilder.Build(_workspace, _lastReport, _lastPrincipalDpi, _lastAuxDpi);
        }
    }

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

    public SceneStore Scenes { get; } = new(SceneStore.DefaultPath);

    public PanelStore PanelFile { get; } = new(PanelStore.DefaultPath);

    /// <summary>Lo último que se guardó en panels.json, para escribir sólo cuando cambia.</summary>
    private List<Panel> _savedPanels = [];

    /// <summary>Carga los paneles guardados (si los hay) en el modelo. Se llama al arrancar.</summary>
    public void LoadPanels()
    {
        var saved = PanelFile.Load();
        lock (_gate)
        {
            if (saved.Count > 0)
            {
                _workspace.Execute(new ReplacePanels(saved));
            }

            _savedPanels = [.. _workspace.Panels];
        }
    }

    /// <summary>Guarda los paneles si cambiaron (nombre, ubicación, presentación, altas o bajas).</summary>
    private void SavePanelsIfChanged()
    {
        if (!_workspace.Panels.SequenceEqual(_savedPanels))
        {
            _savedPanels = [.. _workspace.Panels];
            PanelFile.Save(_savedPanels);
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
                    return new SceneWindow(MinSizeCatalog.KeyFor(snapshot), w.Label, snapshot.Title, w.PanelId, placement);
                })
                .ToList();
            var panels = _workspace.Panels
                .Select(p => new ScenePanel(p.Id, p.Name, AuxIndex(p.Display), p.Side, p.Template))
                .ToList();
            scene = new SavedScene(name.Trim(), windows) { Panels = panels };
        }

        Scenes.Upsert(scene);
        ScenesChanged?.Invoke();
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
            .Select(m => new SceneEntry(m.Window.Handle, m.Entry.Label, m.Entry.PanelId ?? Workspace.WorkPanel, m.Entry.Placement))
            .ToList();
        var panels = scene.Panels
            .Select(p => new Panel(p.Id, p.Name, DisplayOf(p.AuxIndex), p.Side, p.Template))
            .ToList();
        var result = Execute(new ApplyScene(entries, panels));
        return new SceneResult(scene.Name, result, match.Missing, notes.Distinct().ToList());
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

    private ControllerResult RunLocked(IReadOnlyList<Command> commands, bool forceLayout = false)
    {
        // Lo que el gestor mueva aquí no es del usuario: el observador calla hasta que haya
        // expectativas nuevas (ADR 0007).
        using var own = _watcher?.OwnChange();
        var result = RunEffectsLocked(commands, forceLayout);
        SyncExpectations(result.Applied);
        SavePanelsIfChanged();
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
                case RestoreEverything:
                    restores.Add(JournalRestorer.Restore(Journal));
                    break;
            }
        }

        SceneApplyReport? applied = null;
        if ((forceLayout || effects.Any(e => e is ApplyLayout)) && _workspace.Windows.Count > 0)
        {
            applied = ApplyLayoutLocked();
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
        var principal = DisplayTopology.Principal(displays);
        foreach (var d in displays)
        {
            _displayNames[d.Key] = d.FriendlyName ?? d.DeviceName;
        }

        var connected = displays.Where(d => d != principal).Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var known in _workspace.KnownDisplays)
        {
            bool present = connected.Contains(known);
            bool wasMissing = _workspace.IsDisplayMissing(known);
            Command command = present ? new DisplayAdded(known) : new DisplayRemoved(known);
            changed |= _workspace.Execute(command).Effects.Count > 0;
            if (present && wasMissing)
            {
                _displayEvents.Add($"Volvió {NameOf(known)}: sus ventanas regresan a él.");
            }
            else if (!present && !wasMissing)
            {
                _displayEvents.Add($"Se desconectó {NameOf(known)}: sus ventanas pasan a los paneles del principal hasta que vuelva.");
            }
        }

        return changed;
    }

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

    private SceneApplyReport ApplyLayoutLocked()
    {
        var displays = DisplayEnumerator.Enumerate();
        var principal = DisplayTopology.Principal(displays) ?? throw new InvalidOperationException("No se encontró ningún monitor.");

        ReconcileDisplaysLocked(displays);

        var c = _workspace.Composition;
        List<WindowSnapshot> Map(IEnumerable<nint> handles) =>
            handles.Where(_snapshots.ContainsKey).Select(h => _snapshots[h]).ToList();

        var assignment = new SceneAssignment(
            Map(c.Context),
            c.Center is { } center && _snapshots.TryGetValue(center, out var w) ? w : null,
            Map(c.Work))
        {
            ContextId = c.Left?.Panel.Id ?? Workspace.ContextPanel,
            ContextTitle = c.Left?.Panel.Name ?? "Contexto",
            WorkId = c.Right?.Panel.Id ?? Workspace.WorkPanel,
            WorkTitle = c.Right?.Panel.Name ?? "Trabajo",
            Parked = Map(c.Parked),
            Aux = c.Aux
                .Select(a => new AuxAssignment(
                    displays.First(d => d.Key == a.Display),
                    a.Panels.Select(v => new PanelAssignment(v.Panel.Id, v.Panel.Name, v.Panel.Side, v.Panel.Template, Map(v.Windows))).ToList()))
                .ToList(),
        };
        var options = new SceneOptions
        {
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
