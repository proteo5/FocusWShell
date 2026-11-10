using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Domain;

/// <summary>Dónde está ahora la ventana. <c>Fullscreen</c> llega en M1b.</summary>
public enum Placement
{
    InPanel,
    Center,
    Parked,
    Fullscreen,
}

/// <summary>
/// Un panel (ADR 0011): entidad con nombre, ubicación y presentación, que se crea, renombra, mueve y borra.
/// </summary>
/// <param name="Id">Identificador estable (no cambia al renombrar).</param>
/// <param name="Display">Monitor auxiliar (su clave estable) o <c>null</c> para el principal.</param>
/// <param name="Side">
/// En el principal, la columna a la que va respecto del foco. En un auxiliar, el orden de izquierda a
/// derecha y, si ese monitor falta, a qué lado del principal van sus ventanas.
/// </param>
/// <param name="Template">Presentación del panel.</param>
public sealed record Panel(string Id, string Name, string? Display, PanelSide Side, PanelTemplate Template = PanelTemplate.Tiles);

/// <summary>
/// Una ventana gestionada. El orden dentro de su panel es su posición en <see cref="Workspace.Windows"/>;
/// en el Centro conserva esa posición, así que al volver recupera su puesto.
/// </summary>
/// <param name="PanelId">Su panel: adonde vuelve si está en el Centro o estacionada.</param>
/// <param name="Detached">
/// "Fuera de layout": el usuario la arrastró, la redimensionó o la maximizó en el principal. El gestor
/// no la mueve hasta que se reencaje, se promueva o se estacione; los demás de su panel ocupan su sitio.
/// </param>
public sealed record ManagedWindow(nint Handle, string Label, string PanelId, Placement Placement, bool Detached = false);

/// <summary>Un panel con las ventanas que el layout coloca en él, en orden.</summary>
public sealed record PanelView(Panel Panel, IReadOnlyList<nint> Windows);

/// <summary>Paneles de un monitor auxiliar, de izquierda a derecha.</summary>
public sealed record AuxComposition(string Display, IReadOnlyList<PanelView> Panels)
{
    /// <summary>Ventanas del primer panel de la izquierda (atajo para las pruebas y la UI).</summary>
    public IReadOnlyList<nint> Context => Panels.FirstOrDefault(p => p.Panel.Side == PanelSide.Left)?.Windows ?? [];

    /// <summary>Ventanas del primer panel de la derecha.</summary>
    public IReadOnlyList<nint> Work => Panels.FirstOrDefault(p => p.Panel.Side == PanelSide.Right)?.Windows ?? [];

    public PanelTemplate ContextTemplate => Panels.FirstOrDefault(p => p.Panel.Side == PanelSide.Left)?.Panel.Template ?? PanelTemplate.Tiles;

    public PanelTemplate WorkTemplate => Panels.FirstOrDefault(p => p.Panel.Side == PanelSide.Right)?.Panel.Template ?? PanelTemplate.Tiles;
}

/// <summary>Qué va en cada región, en orden: lo que los motores de layout necesitan.</summary>
/// <param name="Left">Panel a la izquierda del foco en el principal (por defecto, Contexto).</param>
/// <param name="Right">Panel a la derecha del foco en el principal (por defecto, Trabajo).</param>
public sealed record SceneComposition(PanelView? Left, nint? Center, PanelView? Right)
{
    public IReadOnlyList<nint> Context => Left?.Windows ?? [];

    public IReadOnlyList<nint> Work => Right?.Windows ?? [];

    public PanelTemplate ContextTemplate => Left?.Panel.Template ?? PanelTemplate.Tiles;

    public PanelTemplate WorkTemplate => Right?.Panel.Template ?? PanelTemplate.Tiles;

    /// <summary>Monitores auxiliares con alguna ventana, por orden de aparición.</summary>
    public IReadOnlyList<AuxComposition> Aux { get; init; } = [];

    /// <summary>Ventanas estacionadas: el aplicador las deja minimizadas.</summary>
    public IReadOnlyList<nint> Parked { get; init; } = [];

    /// <summary>Ventanas que el layout coloca (sin las estacionadas ni las que están fuera de layout).</summary>
    public IEnumerable<nint> All => Context.Concat(Center is { } c ? [c] : []).Concat(Work)
        .Concat(Aux.SelectMany(a => a.Panels.SelectMany(p => p.Windows)));
}

#pragma warning disable CA1716 // Nombres del modelo de dominio (02-modelo-de-dominio.md).

/// <summary>Comandos del núcleo (02-modelo-de-dominio.md, "Ubicaciones y transiciones").</summary>
public abstract record Command;

/// <summary>Empieza a gestionar una ventana libre y la pone en un panel.</summary>
public sealed record Adopt(nint Handle, string Label, string PanelId) : Command;

/// <summary>Lleva la ventana al Centro; la que estaba vuelve a su panel. Activa la ventana.</summary>
public sealed record Promote(nint Handle) : Command;

/// <summary>Devuelve la ventana del Centro a su panel. El Centro queda vacío.</summary>
public sealed record ReturnToPanel(nint Handle) : Command;

/// <summary>Promueve la ventana que ocupó el Centro antes (CA2).</summary>
public sealed record SwapWithPrevious : Command;

/// <summary>Cambia la ventana de panel (al final del otro). Si está en el Centro, cambia adónde vuelve.</summary>
public sealed record SendTo(nint Handle, string PanelId) : Command;

/// <summary>Crea un panel.</summary>
public sealed record CreatePanel(Panel Panel) : Command;

/// <summary>Cambia el nombre de un panel. No mueve nada.</summary>
public sealed record RenamePanel(string PanelId, string Name) : Command;

/// <summary>Lleva un panel a otro monitor o al otro lado del foco, con sus ventanas.</summary>
public sealed record MovePanel(string PanelId, string? Display, PanelSide Side) : Command;

/// <summary>Cambia la presentación de un panel. <c>Fill</c> sólo vale en monitores auxiliares.</summary>
public sealed record SetPresentation(string PanelId, PanelTemplate Template) : Command;

/// <summary>Borra un panel. Sus ventanas quedan libres, donde estén (ADR 0011).</summary>
public sealed record DeletePanel(string PanelId) : Command;

/// <summary>
/// Evento externo: se desconectó un monitor auxiliar. Mientras falte, las ventanas de sus paneles se
/// colocan en el panel del mismo lado del principal, nunca en el Centro, pero siguen siendo de su panel.
/// </summary>
public sealed record DisplayRemoved(string Display) : Command;

/// <summary>Evento externo: volvió un monitor que faltaba. Sus paneles vuelven a él.</summary>
public sealed record DisplayAdded(string Display) : Command;

/// <summary>Aparta la ventana (minimizada) sin quitarle su panel (CA4).</summary>
public sealed record Park(nint Handle) : Command;

/// <summary>Devuelve una ventana estacionada a su panel, en el puesto que tenía (CA4).</summary>
public sealed record Restore(nint Handle) : Command;

/// <summary>Deja la ventana fuera de layout: el usuario la movió y el gestor no pelea con él.</summary>
public sealed record Detach(nint Handle) : Command;

/// <summary>"Reencajar": devuelve al layout una ventana que estaba fuera.</summary>
public sealed record Reattach(nint Handle) : Command;

/// <summary>
/// La ventana vuelve a su sitio en el layout (p. ej. tras arrastrarla o redimensionarla). Si estaba
/// fuera de layout, vuelve a entrar.
/// </summary>
public sealed record SnapBack(nint Handle) : Command;

/// <summary>La pone la primera de su panel: en una pila, la pestaña visible.</summary>
public sealed record MakeActiveInPanel(nint Handle) : Command;

/// <summary>
/// Aplica una escena guardada ya emparejada. Sus paneles se <b>suman</b> a los del usuario: uno que ya
/// existe conserva su nombre y su ubicación y toma la presentación de la escena; uno nuevo se añade.
/// Una ventana cuyo panel no se puede resolver queda libre. Las gestionadas que no están en la escena
/// vuelven a su sitio original.
/// </summary>
public sealed record ApplyScene(IReadOnlyList<SceneEntry> Entries, IReadOnlyList<Panel> Panels) : Command;

/// <summary>Sustituye todos los paneles (al arrancar, con los guardados). Sólo sin ventanas gestionadas.</summary>
public sealed record ReplacePanels(IReadOnlyList<Panel> Panels) : Command;

/// <summary>Devuelve la ventana a su estado original y deja de gestionarla.</summary>
public sealed record Release(nint Handle) : Command;

/// <summary><c>Release</c> de todas: el botón de pánico (CA6).</summary>
public sealed record RestoreAll : Command;

/// <summary>Evento externo: la ventana se cerró. No promueve otra en su lugar.</summary>
public sealed record WindowClosed(nint Handle) : Command;

/// <summary>Efecto que la capa Win32 debe ejecutar, en este orden.</summary>
public abstract record Effect;

/// <summary>Capturar el estado original y escribirlo en el diario ANTES de mover (ADR 0005).</summary>
public sealed record CaptureOriginal(nint Handle) : Effect;

/// <summary>Aplicar el estado original guardado en el diario.</summary>
public sealed record RestoreOriginal(nint Handle) : Effect;

/// <summary>Olvidar el estado original sin aplicarlo: la ventana queda libre donde está.</summary>
public sealed record ForgetOriginal(nint Handle) : Effect;

/// <summary>Aplicar todo el diario, incluidas ventanas que el modelo no conozca.</summary>
public sealed record RestoreEverything : Effect;

/// <summary>Recalcular y aplicar el layout de <see cref="Workspace.Composition"/>.</summary>
public sealed record ApplyLayout : Effect;

/// <summary>Dar el foco a la ventana (sólo <c>Promote</c>).</summary>
public sealed record Activate(nint Handle) : Effect;

#pragma warning restore CA1716

/// <summary>Resultado de un comando: aceptado con sus efectos, o rechazado con el motivo.</summary>
public sealed record CommandOutcome(bool Accepted, string? Rejection, IReadOnlyList<Effect> Effects)
{
    public static CommandOutcome Accept(params Effect[] effects) => new(true, null, effects);

    public static CommandOutcome Reject(string reason) => new(false, reason, []);
}

/// <summary>
/// Estado lógico del gestor: qué paneles hay, qué ventanas gestiona, en qué panel está cada una y dónde.
/// Es puro: no conoce Win32 y no mueve nada; cada comando devuelve los efectos que otra capa ejecuta.
/// Invariantes (02-modelo-de-dominio.md): como máximo una ventana en el Centro; un comando sólo cambia
/// el panel de la ventana que nombra; <c>RestoreAll</c> deja el modelo sin ventanas.
/// </summary>
public sealed class Workspace
{
    /// <summary>Panel por defecto a la izquierda del foco.</summary>
    public const string ContextPanel = "contexto";

    /// <summary>Panel por defecto a la derecha del foco.</summary>
    public const string WorkPanel = "trabajo";

    private readonly List<Panel> _panels = [];
    private readonly List<ManagedWindow> _windows = [];

    /// <summary>Ventanas que ocuparon el Centro, de la más reciente a la más antigua.</summary>
    private readonly List<nint> _centerHistory = [];

    /// <summary>Monitores auxiliares desconectados: sus paneles se colocan en el principal hasta que vuelvan.</summary>
    private readonly HashSet<string> _missingDisplays = new(StringComparer.OrdinalIgnoreCase);

    public Workspace()
    {
        _panels.AddRange(DefaultPanels());
    }

    /// <summary>Los dos paneles con los que empieza todo: uno a cada lado del foco.</summary>
    public static IReadOnlyList<Panel> DefaultPanels() =>
    [
        new Panel(ContextPanel, "Contexto", null, PanelSide.Left),
        new Panel(WorkPanel, "Trabajo", null, PanelSide.Right),
    ];

    public IReadOnlyList<Panel> Panels => _panels;

    public IReadOnlyList<ManagedWindow> Windows => _windows;

    public IReadOnlyList<nint> CenterHistory => _centerHistory;

    public ManagedWindow? Center => _windows.FirstOrDefault(w => w.Placement == Placement.Center);

    public ManagedWindow? Find(nint handle) => _windows.FirstOrDefault(w => w.Handle == handle);

    public Panel? FindPanel(string id) => _panels.FirstOrDefault(p => p.Id == id);

    public bool IsManaged(nint handle) => Find(handle) is not null;

    public bool IsDisplayMissing(string? display) => display is not null && _missingDisplays.Contains(display);

    /// <summary>Monitores auxiliares que el modelo conoce: los de sus paneles y los que faltan.</summary>
    public IReadOnlyCollection<string> KnownDisplays =>
        _panels.Where(p => p.Display is not null).Select(p => p.Display!).Concat(_missingDisplays)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Panel donde se coloca de verdad la ventana: el suyo o, si su monitor falta, el del mismo lado en el
    /// principal (o el otro, si ese lado no tiene panel).
    /// </summary>
    public Panel EffectivePanel(ManagedWindow w)
    {
        ArgumentNullException.ThrowIfNull(w);
        var own = FindPanel(w.PanelId) ?? _panels[0];
        if (!IsDisplayMissing(own.Display))
        {
            return own;
        }

        return PrincipalPanel(own.Side) ?? _panels.First(p => p.Display is null);
    }

    private Panel? PrincipalPanel(PanelSide side) => _panels.FirstOrDefault(p => p.Display is null && p.Side == side);

    public SceneComposition Composition
    {
        get
        {
            PanelView? View(Panel? p) => p is null ? null : new PanelView(p, InPanel(p));
            var aux = _panels
                .Where(p => p.Display is not null && !IsDisplayMissing(p.Display))
                .GroupBy(p => p.Display!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new AuxComposition(
                    g.Key,
                    g.OrderBy(p => p.Side).Select(p => new PanelView(p, InPanel(p))).Where(v => v.Windows.Count > 0).ToList()))
                .Where(a => a.Panels.Count > 0)
                .ToList();
            return new SceneComposition(
                View(PrincipalPanel(PanelSide.Left)),
                Center is { Detached: false } center ? center.Handle : null,
                View(PrincipalPanel(PanelSide.Right)))
            {
                Parked = _windows.Where(w => w.Placement == Placement.Parked).Select(w => w.Handle).ToList(),
                Aux = aux,
            };
        }
    }

    public CommandOutcome Execute(Command command) => command switch
    {
        Adopt c => DoAdopt(c),
        Promote c => DoPromote(c.Handle),
        ReturnToPanel c => DoReturnToPanel(c.Handle),
        SwapWithPrevious => DoSwapWithPrevious(),
        SendTo c => DoSendTo(c),
        CreatePanel c => DoCreatePanel(c.Panel),
        RenamePanel c => DoRenamePanel(c),
        MovePanel c => DoMovePanel(c),
        SetPresentation c => DoSetPresentation(c),
        DeletePanel c => DoDeletePanel(c.PanelId),
        DisplayRemoved c => DoDisplayRemoved(c.Display),
        DisplayAdded c => DoDisplayAdded(c.Display),
        Park c => DoPark(c.Handle),
        Restore c => DoRestore(c.Handle),
        Detach c => DoDetach(c.Handle),
        Reattach c => DoReattach(c.Handle),
        SnapBack c => DoSnapBack(c.Handle),
        MakeActiveInPanel c => DoMakeActiveInPanel(c.Handle),
        ApplyScene c => DoApplyScene(c),
        ReplacePanels c => DoReplacePanels(c.Panels),
        Release c => DoRelease(c.Handle),
        RestoreAll => DoRestoreAll(),
        WindowClosed c => DoWindowClosed(c.Handle),
        null => throw new ArgumentNullException(nameof(command)),
        _ => throw new ArgumentException($"Comando desconocido: {command.GetType().Name}", nameof(command)),
    };

    /// <summary>Ventanas que el layout coloca en este panel, en orden.</summary>
    private List<nint> InPanel(Panel panel) =>
        _windows.Where(w => w.Placement == Placement.InPanel && !w.Detached && EffectivePanel(w).Id == panel.Id)
            .Select(w => w.Handle).ToList();

    private void Replace(ManagedWindow updated) =>
        _windows[_windows.FindIndex(w => w.Handle == updated.Handle)] = updated;

    private bool AnyWindowShownOn(string display) =>
        _windows.Any(w => FindPanel(w.PanelId)?.Display is { } d && string.Equals(d, display, StringComparison.OrdinalIgnoreCase));

    // --- Ventanas -----------------------------------------------------------------------------------

    private CommandOutcome DoAdopt(Adopt c)
    {
        if (IsManaged(c.Handle))
        {
            return CommandOutcome.Reject($"{c.Label} ya está gestionada.");
        }

        if (FindPanel(c.PanelId) is null)
        {
            return CommandOutcome.Reject($"No existe el panel «{c.PanelId}».");
        }

        _windows.Add(new ManagedWindow(c.Handle, c.Label, c.PanelId, Placement.InPanel));
        return CommandOutcome.Accept(new CaptureOriginal(c.Handle), new ApplyLayout());
    }

    private CommandOutcome DoPromote(nint handle)
    {
        var w = Find(handle);
        if (w is null)
        {
            return CommandOutcome.Reject("La ventana no está gestionada.");
        }

        if (w.Placement == Placement.Center)
        {
            return CommandOutcome.Reject($"{w.Label} ya está en el Centro.");
        }

        if (w.Placement is not (Placement.InPanel or Placement.Parked))
        {
            return CommandOutcome.Reject($"{w.Label} no se puede promover desde {w.Placement}.");
        }

        if (Center is { } previous)
        {
            // La anterior vuelve a su panel, al mismo puesto que tenía, y queda como "anterior".
            Replace(previous with { Placement = Placement.InPanel });
            _centerHistory.Remove(previous.Handle);
            _centerHistory.Insert(0, previous.Handle);
        }

        _centerHistory.Remove(handle);
        Replace(w with { Placement = Placement.Center, Detached = false });
        return CommandOutcome.Accept(new ApplyLayout(), new Activate(handle));
    }

    private CommandOutcome DoReturnToPanel(nint handle)
    {
        var w = Find(handle);
        if (w is not { Placement: Placement.Center })
        {
            return CommandOutcome.Reject("Sólo se puede devolver al panel la ventana del Centro.");
        }

        Replace(w with { Placement = Placement.InPanel });
        _centerHistory.Remove(handle);
        _centerHistory.Insert(0, handle);
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoSwapWithPrevious()
    {
        var current = Center?.Handle;
        var previous = _centerHistory.FirstOrDefault(h => h != current && IsManaged(h));
        return previous == 0
            ? CommandOutcome.Reject("No hay una ventana anterior en el Centro.")
            : DoPromote(previous);
    }

    private CommandOutcome DoSendTo(SendTo c)
    {
        var w = Find(c.Handle);
        if (w is null)
        {
            return CommandOutcome.Reject("La ventana no está gestionada.");
        }

        if (FindPanel(c.PanelId) is null)
        {
            return CommandOutcome.Reject($"No existe el panel «{c.PanelId}».");
        }

        if (w.Placement == Placement.Fullscreen)
        {
            return CommandOutcome.Reject($"{w.Label} está en pantalla completa.");
        }

        if (w.PanelId == c.PanelId)
        {
            return CommandOutcome.Accept();
        }

        // Pasa al final del otro panel. Si está en el Centro, sigue ahí: sólo cambia adónde vuelve.
        _windows.Remove(w);
        _windows.Add(w with { PanelId = c.PanelId });
        return w.Placement == Placement.InPanel && !w.Detached
            ? CommandOutcome.Accept(new ApplyLayout())
            : CommandOutcome.Accept();
    }

    // --- Paneles ------------------------------------------------------------------------------------

    private string? ValidateLocation(string? display, PanelSide side, PanelTemplate template, string? exceptId)
    {
        if (display is null && template == PanelTemplate.Fill)
        {
            return "Fill sólo vale en monitores auxiliares: en el principal manda el Centro.";
        }

        // Etapa 1: un panel por columna en el principal (dos por columna llega con las vistas, ADR 0012).
        if (display is null && _panels.Any(p => p.Id != exceptId && p.Display is null && p.Side == side))
        {
            return $"Ya hay un panel a la {(side == PanelSide.Left ? "izquierda" : "derecha")} del foco.";
        }

        return null;
    }

    private CommandOutcome DoCreatePanel(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (string.IsNullOrWhiteSpace(panel.Id) || string.IsNullOrWhiteSpace(panel.Name))
        {
            return CommandOutcome.Reject("El panel necesita un identificador y un nombre.");
        }

        if (FindPanel(panel.Id) is not null)
        {
            return CommandOutcome.Reject($"Ya existe un panel «{panel.Id}».");
        }

        if (ValidateLocation(panel.Display, panel.Side, panel.Template, null) is { } problem)
        {
            return CommandOutcome.Reject(problem);
        }

        _panels.Add(panel with { Name = panel.Name.Trim() });
        return CommandOutcome.Accept();
    }

    private CommandOutcome DoRenamePanel(RenamePanel c)
    {
        if (FindPanel(c.PanelId) is not { } panel)
        {
            return CommandOutcome.Reject($"No existe el panel «{c.PanelId}».");
        }

        if (string.IsNullOrWhiteSpace(c.Name))
        {
            return CommandOutcome.Reject("El nombre no puede estar vacío.");
        }

        _panels[_panels.IndexOf(panel)] = panel with { Name = c.Name.Trim() };
        return CommandOutcome.Accept();
    }

    private CommandOutcome DoMovePanel(MovePanel c)
    {
        if (FindPanel(c.PanelId) is not { } panel)
        {
            return CommandOutcome.Reject($"No existe el panel «{c.PanelId}».");
        }

        if (panel.Display == c.Display && panel.Side == c.Side)
        {
            return CommandOutcome.Accept();
        }

        if (ValidateLocation(c.Display, c.Side, panel.Template, panel.Id) is { } problem)
        {
            return CommandOutcome.Reject(problem);
        }

        _panels[_panels.IndexOf(panel)] = panel with { Display = c.Display, Side = c.Side };
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoSetPresentation(SetPresentation c)
    {
        if (FindPanel(c.PanelId) is not { } panel)
        {
            return CommandOutcome.Reject($"No existe el panel «{c.PanelId}».");
        }

        if (panel.Display is null && c.Template == PanelTemplate.Fill)
        {
            return CommandOutcome.Reject("Fill sólo vale en monitores auxiliares: en el principal manda el Centro.");
        }

        if (panel.Template == c.Template)
        {
            return CommandOutcome.Accept();
        }

        _panels[_panels.IndexOf(panel)] = panel with { Template = c.Template };
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoDeletePanel(string id)
    {
        if (FindPanel(id) is not { } panel)
        {
            return CommandOutcome.Reject($"No existe el panel «{id}».");
        }

        if (panel.Display is null && _panels.Count(p => p.Display is null) == 1)
        {
            return CommandOutcome.Reject("Tiene que quedar al menos un panel en el monitor principal.");
        }

        // Sus ventanas quedan libres donde están: se olvida su estado original sin aplicarlo.
        var effects = new List<Effect>();
        foreach (var w in _windows.Where(w => w.PanelId == id).ToList())
        {
            Forget(w.Handle);
            effects.Add(new ForgetOriginal(w.Handle));
        }

        _panels.Remove(panel);
        effects.Add(new ApplyLayout());
        return CommandOutcome.Accept([.. effects]);
    }

    // --- Monitores ----------------------------------------------------------------------------------

    private CommandOutcome DoDisplayRemoved(string display)
    {
        // Se recuerda aunque no tenga ventanas: si después se le envía alguna, debe ir al principal.
        bool added = _missingDisplays.Add(display);
        return added && AnyWindowShownOn(display)
            ? CommandOutcome.Accept(new ApplyLayout())
            : CommandOutcome.Accept();
    }

    private CommandOutcome DoDisplayAdded(string display)
    {
        bool removed = _missingDisplays.Remove(display);
        return removed && AnyWindowShownOn(display)
            ? CommandOutcome.Accept(new ApplyLayout())
            : CommandOutcome.Accept();
    }

    // --- Estacionar, fuera de layout, pestaña visible ----------------------------------------------

    private CommandOutcome DoPark(nint handle)
    {
        var w = Find(handle);
        if (w is null)
        {
            return CommandOutcome.Reject("La ventana no está gestionada.");
        }

        if (w.Placement == Placement.Parked)
        {
            return CommandOutcome.Accept();
        }

        if (w.Placement is not (Placement.InPanel or Placement.Center))
        {
            return CommandOutcome.Reject($"{w.Label} no se puede estacionar desde {w.Placement}.");
        }

        if (w.Placement == Placement.Center)
        {
            // El Centro queda vacío; la ventana queda como "anterior" para volver con un atajo.
            _centerHistory.Remove(handle);
            _centerHistory.Insert(0, handle);
        }

        Replace(w with { Placement = Placement.Parked, Detached = false });
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoRestore(nint handle)
    {
        var w = Find(handle);
        if (w is not { Placement: Placement.Parked })
        {
            return CommandOutcome.Reject("Sólo se puede restaurar una ventana estacionada.");
        }

        // Conserva su puesto en el panel: su posición en la lista no cambió al estacionarla.
        Replace(w with { Placement = Placement.InPanel });
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoDetach(nint handle)
    {
        var w = Find(handle);
        if (w is null || w.Detached || w.Placement == Placement.Parked)
        {
            return CommandOutcome.Accept();
        }

        Replace(w with { Detached = true });
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoReattach(nint handle)
    {
        var w = Find(handle);
        if (w is not { Detached: true })
        {
            return CommandOutcome.Reject("La ventana no está fuera de layout.");
        }

        Replace(w with { Detached = false });
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoSnapBack(nint handle)
    {
        var w = Find(handle);
        if (w is null || w.Placement == Placement.Parked)
        {
            return CommandOutcome.Accept();
        }

        if (w.Detached)
        {
            Replace(w with { Detached = false });
        }

        // Aunque el modelo no cambie, el layout se vuelve a aplicar: es lo que devuelve la ventana.
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoMakeActiveInPanel(nint handle)
    {
        var w = Find(handle);
        if (w is not { Placement: Placement.InPanel })
        {
            return CommandOutcome.Accept();
        }

        var panel = EffectivePanel(w);
        int first = _windows.FindIndex(x => x.Placement == Placement.InPanel && !x.Detached && EffectivePanel(x).Id == panel.Id);
        int index = _windows.IndexOf(w);
        if (first < 0 || first == index)
        {
            return CommandOutcome.Accept();
        }

        _windows.RemoveAt(index);
        _windows.Insert(first, w);
        return CommandOutcome.Accept(new ApplyLayout());
    }

    /// <summary>
    /// Si el layout la tiene minimizada sin que esté estacionada: no es la primera de un panel en pestañas
    /// o Fill, o es de un panel tapado por el Fill de otro en el mismo auxiliar.
    /// </summary>
    public bool IsHiddenByLayout(nint handle)
    {
        var w = Find(handle);
        if (w is not { Placement: Placement.InPanel, Detached: false })
        {
            return false;
        }

        var panel = EffectivePanel(w);
        if (panel.Template is PanelTemplate.Stack or PanelTemplate.Fill)
        {
            return InPanel(panel)[0] != handle;
        }

        // Otro panel del mismo auxiliar en Fill la tapa.
        return panel.Display is { } display && !IsDisplayMissing(display)
            && _panels.Any(p => p.Id != panel.Id && string.Equals(p.Display, display, StringComparison.OrdinalIgnoreCase)
                && p.Template == PanelTemplate.Fill && InPanel(p).Count > 0);
    }

    /// <summary>
    /// Traduce un cambio que hizo otro (el usuario, otra app) en comandos del modelo
    /// (02-modelo-de-dominio, "Eventos externos"). Nunca promueve: el foco no mueve nada (CA7).
    /// </summary>
    public IReadOnlyList<Command> Interpret(nint handle, ExternalChange change)
    {
        var w = Find(handle);
        if (w is null)
        {
            return [];
        }

        var panel = EffectivePanel(w);
        bool onAux = panel.Display is not null;
        return change switch
        {
            ExternalChange.Closed => [new WindowClosed(handle)],

            // Minimizar con el botón de Windows es estacionar (salvo que ya lo estuviera).
            ExternalChange.Minimized when w.Placement == Placement.Parked => [],
            ExternalChange.Minimized => [new Park(handle)],

            // Mostrarla desde la barra de tareas: si estaba estacionada, vuelve a su panel; si la
            // escondía la pila, pasa a ser la pestaña visible.
            ExternalChange.Shown when w.Placement == Placement.Parked => [new Restore(handle)],
            ExternalChange.Shown when IsHiddenByLayout(handle) => [new MakeActiveInPanel(handle)],
            ExternalChange.Shown => [],

            // Maximizar en un auxiliar es pedir Fill para su panel; en el principal, salir del layout.
            ExternalChange.Maximized when w.Placement == Placement.InPanel && onAux =>
                [new MakeActiveInPanel(handle), new SetPresentation(panel.Id, PanelTemplate.Fill)],
            ExternalChange.Maximized => [new Detach(handle)],

            // Des-maximizar la ventana de un panel en Fill devuelve el panel a Tiles; en otro caso,
            // la ventana vuelve a su sitio.
            ExternalChange.Unmaximized when onAux && panel.Template == PanelTemplate.Fill =>
                [new SetPresentation(panel.Id, PanelTemplate.Tiles)],
            ExternalChange.Unmaximized => [new SnapBack(handle)],

            // Arrastrarla o redimensionarla: al soltarla vuelve a su sitio. (Soltarla sobre otro panel
            // o sobre el foco lo decide quien conoce la geometría: ver ShellController.)
            ExternalChange.MovedOrResized => [new SnapBack(handle)],
            _ => [],
        };
    }

    // --- Escenas y salida ---------------------------------------------------------------------------

    private CommandOutcome DoReplacePanels(IReadOnlyList<Panel> panels)
    {
        if (_windows.Count > 0)
        {
            return CommandOutcome.Reject("Los paneles sólo se sustituyen sin ventanas gestionadas.");
        }

        var valid = new List<Panel>();
        foreach (var p in panels.DistinctBy(p => p.Id))
        {
            // Fill no vale en el principal; dos paneles en la misma columna, tampoco (etapa 1).
            var panel = p.Display is null && p.Template == PanelTemplate.Fill ? p with { Template = PanelTemplate.Tiles } : p;
            if (panel.Display is null && valid.Any(x => x.Display is null && x.Side == panel.Side))
            {
                continue;
            }

            valid.Add(panel);
        }

        if (!valid.Any(p => p.Display is null))
        {
            return CommandOutcome.Reject("Tiene que quedar al menos un panel en el monitor principal.");
        }

        _panels.Clear();
        _panels.AddRange(valid);
        return CommandOutcome.Accept();
    }

    private CommandOutcome DoApplyScene(ApplyScene c)
    {
        // Los paneles de la escena se suman a los del usuario. "map" traduce cada panel de la escena al
        // panel real donde irán sus ventanas.
        var map = new Dictionary<string, string>();
        foreach (var p in c.Panels.DistinctBy(p => p.Id))
        {
            var wanted = p.Display is null && p.Template == PanelTemplate.Fill ? p with { Template = PanelTemplate.Tiles } : p;
            if (FindPanel(wanted.Id) is { } existing)
            {
                // Ya existe: conserva su nombre y su ubicación; toma la presentación de la escena si vale ahí.
                if (existing.Template != wanted.Template && !(existing.Display is null && wanted.Template == PanelTemplate.Fill))
                {
                    _panels[_panels.IndexOf(existing)] = existing with { Template = wanted.Template };
                }

                map[wanted.Id] = existing.Id;
            }
            else if (wanted.Display is null && PrincipalPanel(wanted.Side) is { } occupant)
            {
                // Esa columna del principal ya tiene panel: sus ventanas van ahí.
                map[wanted.Id] = occupant.Id;
            }
            else
            {
                _panels.Add(wanted);
                map[wanted.Id] = wanted.Id;
            }
        }

        // Una ventana cuyo panel no se resuelve queda libre (ADR 0011): no se adopta.
        var entries = c.Entries
            .DistinctBy(e => e.Handle)
            .Select(e => map.TryGetValue(e.PanelId, out var id) ? e with { PanelId = id } : FindPanel(e.PanelId) is not null ? e : null)
            .OfType<SceneEntry>()
            .ToList();
        var keep = entries.Select(e => e.Handle).ToHashSet();
        var effects = new List<Effect>();

        // Las que salen de la escena vuelven a su sitio; las nuevas se capturan antes de moverlas.
        foreach (var gone in _windows.Where(w => !keep.Contains(w.Handle)).ToList())
        {
            effects.Add(new RestoreOriginal(gone.Handle));
        }

        foreach (var e in entries.Where(e => !IsManaged(e.Handle)))
        {
            effects.Add(new CaptureOriginal(e.Handle));
        }

        var previousCenter = Center?.Handle;
        var center = entries.FirstOrDefault(e => e.Placement == ScenePlacement.Center)?.Handle;
        _windows.Clear();
        foreach (var e in entries)
        {
            var placement = e.Placement switch
            {
                ScenePlacement.Center when e.Handle == center => Placement.Center,
                ScenePlacement.Parked => Placement.Parked,
                _ => Placement.InPanel,
            };
            _windows.Add(new ManagedWindow(e.Handle, e.Label, e.PanelId, placement));
        }

        _centerHistory.RemoveAll(h => !keep.Contains(h) || h == center);
        if (previousCenter is { } prev && prev != center && keep.Contains(prev))
        {
            _centerHistory.Insert(0, prev);
        }

        effects.Add(new ApplyLayout());
        if (center is { } h)
        {
            effects.Add(new Activate(h));
        }

        return CommandOutcome.Accept([.. effects]);
    }

    private CommandOutcome DoRelease(nint handle)
    {
        var w = Find(handle);
        if (w is null)
        {
            return CommandOutcome.Reject("La ventana no está gestionada.");
        }

        Forget(handle);
        return CommandOutcome.Accept(new RestoreOriginal(handle), new ApplyLayout());
    }

    private CommandOutcome DoRestoreAll()
    {
        // Los paneles se conservan: sólo las ventanas vuelven a ser libres.
        _windows.Clear();
        _centerHistory.Clear();
        return CommandOutcome.Accept(new RestoreEverything());
    }

    private CommandOutcome DoWindowClosed(nint handle)
    {
        if (!IsManaged(handle))
        {
            return CommandOutcome.Accept();
        }

        // Si era la del Centro, el Centro queda vacío: no se promueve otra.
        Forget(handle);
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private void Forget(nint handle)
    {
        _windows.RemoveAll(w => w.Handle == handle);
        _centerHistory.Remove(handle);
    }
}
