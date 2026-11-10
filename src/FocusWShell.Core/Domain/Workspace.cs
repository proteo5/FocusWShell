using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Domain;

/// <summary>Panel al que pertenece una ventana. Persistente: sólo cambia con <see cref="SendTo"/>.</summary>
public enum Home
{
    Work,
    Context,
}

/// <summary>Dónde está ahora la ventana. <c>Parked</c> y <c>Fullscreen</c> llegan en M1/M2.</summary>
public enum Placement
{
    InPanel,
    Center,
    Parked,
    Fullscreen,
}

/// <summary>
/// Una ventana gestionada. El orden dentro de su panel es su posición en <see cref="Workspace.Windows"/>;
/// en el Centro conserva esa posición, así que al volver recupera su puesto.
/// </summary>
/// <param name="Display">Monitor auxiliar de su panel, o <c>null</c> si su panel está en el principal (ADR 0008).</param>
/// <param name="Detached">
/// "Fuera de layout": el usuario la arrastró, la redimensionó o la maximizó en el principal. El gestor
/// no la mueve hasta que se reencaje, se promueva o se estacione; los demás de su panel ocupan su sitio.
/// </param>
public sealed record ManagedWindow(nint Handle, string Label, Home Home, Placement Placement, string? Display = null, bool Detached = false);

/// <summary>Paneles de un monitor auxiliar, en orden.</summary>
public sealed record AuxComposition(
    string Display,
    IReadOnlyList<nint> Context,
    IReadOnlyList<nint> Work,
    PanelTemplate ContextTemplate,
    PanelTemplate WorkTemplate);

/// <summary>Qué va en cada región, en orden: lo que los motores de layout necesitan.</summary>
/// <param name="Context">Panel de Contexto del monitor principal.</param>
/// <param name="Work">Panel de Trabajo del monitor principal.</param>
public sealed record SceneComposition(IReadOnlyList<nint> Context, nint? Center, IReadOnlyList<nint> Work)
{
    public PanelTemplate ContextTemplate { get; init; } = PanelTemplate.Tiles;

    public PanelTemplate WorkTemplate { get; init; } = PanelTemplate.Tiles;

    /// <summary>Monitores auxiliares con alguna ventana, por orden de aparición.</summary>
    public IReadOnlyList<AuxComposition> Aux { get; init; } = [];

    /// <summary>Ventanas estacionadas: el aplicador las deja minimizadas.</summary>
    public IReadOnlyList<nint> Parked { get; init; } = [];

    /// <summary>Ventanas que el layout coloca (sin las estacionadas ni las que están fuera de layout).</summary>
    public IEnumerable<nint> All => Context.Concat(Center is { } c ? [c] : []).Concat(Work)
        .Concat(Aux.SelectMany(a => a.Context.Concat(a.Work)));
}

#pragma warning disable CA1716 // Nombres del modelo de dominio (02-modelo-de-dominio.md).

/// <summary>Comandos del núcleo (02-modelo-de-dominio.md, "Ubicaciones y transiciones").</summary>
public abstract record Command;

/// <summary>Empieza a gestionar una ventana elegible y la pone en su panel (en el principal o en un auxiliar).</summary>
public sealed record Adopt(nint Handle, string Label, Home Home, string? Display = null) : Command;

/// <summary>Lleva la ventana al Centro; la que estaba vuelve a su hogar. Activa la ventana.</summary>
public sealed record Promote(nint Handle) : Command;

/// <summary>Devuelve la ventana del Centro a su panel. El Centro queda vacío.</summary>
public sealed record ReturnToPanel(nint Handle) : Command;

/// <summary>Promueve la ventana que ocupó el Centro antes (CA2).</summary>
public sealed record SwapWithPrevious : Command;

/// <summary>Cambia el hogar de la ventana. Si estaba en su panel, pasa al otro.</summary>
public sealed record SendTo(nint Handle, Home Home) : Command;

/// <summary>Lleva el panel de la ventana a otro monitor: <c>null</c> es el principal. No cambia su hogar.</summary>
public sealed record SendToDisplay(nint Handle, string? Display) : Command;

/// <summary>Cambia el template de un panel. <c>Fill</c> sólo vale en monitores auxiliares.</summary>
public sealed record SetTemplate(string? Display, Home Home, PanelTemplate Template) : Command;

/// <summary>
/// Evento externo: se desconectó un monitor auxiliar. Mientras falte, sus ventanas se colocan en los
/// paneles de su mismo hogar en el principal, nunca en el Centro, pero siguen siendo de ese monitor.
/// </summary>
public sealed record DisplayRemoved(string Display) : Command;

/// <summary>Evento externo: volvió un monitor que faltaba. Sus ventanas vuelven a él, con sus templates.</summary>
public sealed record DisplayAdded(string Display) : Command;

/// <summary>Aparta la ventana (minimizada) sin quitarle su hogar. Aparece como estacionada en su panel (CA4).</summary>
public sealed record Park(nint Handle) : Command;

/// <summary>Devuelve una ventana estacionada a su panel, en el puesto que tenía (CA4).</summary>
public sealed record Restore(nint Handle) : Command;

/// <summary>Deja la ventana fuera de layout: el usuario la movió y el gestor no pelea con él.</summary>
public sealed record Detach(nint Handle) : Command;

/// <summary>"Reencajar": devuelve al layout una ventana que estaba fuera.</summary>
public sealed record Reattach(nint Handle) : Command;

/// <summary>La pone la primera de su panel: en una pila, la pestaña visible.</summary>
public sealed record MakeActiveInPanel(nint Handle) : Command;

/// <summary>
/// Aplica una escena guardada ya emparejada: estas ventanas, en este orden y con estos templates.
/// Las gestionadas que no están en la escena vuelven a su sitio original.
/// </summary>
public sealed record ApplyScene(IReadOnlyList<SceneEntry> Entries, IReadOnlyList<(string? Display, Home Home, PanelTemplate Template)> Templates) : Command;

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
/// Estado lógico del gestor: qué ventanas gestiona, su hogar y su ubicación. Es puro: no conoce
/// Win32 y no mueve nada; cada comando devuelve los efectos que otra capa ejecuta.
/// Invariantes (02-modelo-de-dominio.md): como máximo una ventana en el Centro; un comando sólo
/// cambia el hogar de la ventana que nombra; <c>RestoreAll</c> deja el modelo vacío.
/// </summary>
public sealed class Workspace
{
    private readonly List<ManagedWindow> _windows = [];

    /// <summary>Ventanas que ocuparon el Centro, de la más reciente a la más antigua.</summary>
    private readonly List<nint> _centerHistory = [];

    /// <summary>Monitores auxiliares desconectados: sus ventanas se colocan en el principal hasta que vuelvan.</summary>
    private readonly HashSet<string> _missingDisplays = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDisplayMissing(string? display) => display is not null && _missingDisplays.Contains(display);

    /// <summary>Monitores auxiliares que el modelo conoce: los de sus ventanas y los que faltan.</summary>
    public IReadOnlyCollection<string> KnownDisplays =>
        _windows.Where(w => w.Display is not null).Select(w => w.Display!).Concat(_missingDisplays)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Monitor donde se coloca de verdad: el suyo o, si falta, el principal.</summary>
    private string? Effective(ManagedWindow w) => IsDisplayMissing(w.Display) ? null : w.Display;

    /// <summary>Template de cada panel, por monitor (<c>null</c> = principal) y hogar. Por defecto, Tiles.</summary>
    private readonly Dictionary<(string? Display, Home Home), PanelTemplate> _templates = [];

    public IReadOnlyList<ManagedWindow> Windows => _windows;

    public IReadOnlyList<nint> CenterHistory => _centerHistory;

    public ManagedWindow? Center => _windows.FirstOrDefault(w => w.Placement == Placement.Center);

    public ManagedWindow? Find(nint handle) => _windows.FirstOrDefault(w => w.Handle == handle);

    public bool IsManaged(nint handle) => Find(handle) is not null;

    public PanelTemplate TemplateOf(string? display, Home home) =>
        _templates.GetValueOrDefault((display, home), PanelTemplate.Tiles);

    public SceneComposition Composition => new(
        InPanel(null, Home.Context),
        Center is { Detached: false } center ? center.Handle : null,
        InPanel(null, Home.Work))
    {
        Parked = _windows.Where(w => w.Placement == Placement.Parked).Select(w => w.Handle).ToList(),
        ContextTemplate = TemplateOf(null, Home.Context),
        WorkTemplate = TemplateOf(null, Home.Work),
        Aux = _windows
            .Where(w => Effective(w) is not null && w.Placement == Placement.InPanel && !w.Detached)
            .Select(w => w.Display!)
            .Distinct()
            .Select(d => new AuxComposition(
                d,
                InPanel(d, Home.Context),
                InPanel(d, Home.Work),
                TemplateOf(d, Home.Context),
                TemplateOf(d, Home.Work)))
            .ToList(),
    };

    public CommandOutcome Execute(Command command) => command switch
    {
        Adopt c => DoAdopt(c),
        Promote c => DoPromote(c.Handle),
        ReturnToPanel c => DoReturnToPanel(c.Handle),
        SwapWithPrevious => DoSwapWithPrevious(),
        SendTo c => DoSendTo(c),
        SendToDisplay c => DoSendToDisplay(c),
        SetTemplate c => DoSetTemplate(c),
        DisplayRemoved c => DoDisplayRemoved(c.Display),
        DisplayAdded c => DoDisplayAdded(c.Display),
        Park c => DoPark(c.Handle),
        Restore c => DoRestore(c.Handle),
        Detach c => DoDetach(c.Handle),
        Reattach c => DoReattach(c.Handle),
        MakeActiveInPanel c => DoMakeActiveInPanel(c.Handle),
        ApplyScene c => DoApplyScene(c),
        Release c => DoRelease(c.Handle),
        RestoreAll => DoRestoreAll(),
        WindowClosed c => DoWindowClosed(c.Handle),
        null => throw new ArgumentNullException(nameof(command)),
        _ => throw new ArgumentException($"Comando desconocido: {command.GetType().Name}", nameof(command)),
    };

    private List<nint> InPanel(string? display, Home home) =>
        _windows.Where(w => Effective(w) == display && w.Home == home && w.Placement == Placement.InPanel && !w.Detached)
            .Select(w => w.Handle).ToList();

    private void Replace(ManagedWindow updated) =>
        _windows[_windows.FindIndex(w => w.Handle == updated.Handle)] = updated;

    private CommandOutcome DoAdopt(Adopt c)
    {
        if (IsManaged(c.Handle))
        {
            return CommandOutcome.Reject($"{c.Label} ya está gestionada.");
        }

        _windows.Add(new ManagedWindow(c.Handle, c.Label, c.Home, Placement.InPanel, c.Display));
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
            // La anterior vuelve a su hogar, al mismo puesto que tenía en su panel, y queda como "anterior".
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

        if (w.Placement == Placement.Fullscreen)
        {
            return CommandOutcome.Reject($"{w.Label} está en pantalla completa.");
        }

        if (w.Home == c.Home)
        {
            return CommandOutcome.Accept();
        }

        // Pasa al final del otro panel.
        _windows.Remove(w);
        _windows.Add(w with { Home = c.Home });
        return w.Placement == Placement.InPanel
            ? CommandOutcome.Accept(new ApplyLayout())
            : CommandOutcome.Accept();
    }

    private CommandOutcome DoSendToDisplay(SendToDisplay c)
    {
        var w = Find(c.Handle);
        if (w is null)
        {
            return CommandOutcome.Reject("La ventana no está gestionada.");
        }

        if (w.Display == c.Display)
        {
            return CommandOutcome.Accept();
        }

        // Pasa al final del panel de su hogar en el otro monitor. Si está en el Centro, sigue ahí:
        // sólo cambia adónde vuelve.
        _windows.Remove(w);
        _windows.Add(w with { Display = c.Display });
        return w.Placement == Placement.InPanel ? CommandOutcome.Accept(new ApplyLayout()) : CommandOutcome.Accept();
    }

    private CommandOutcome DoSetTemplate(SetTemplate c)
    {
        if (c.Display is null && c.Template == PanelTemplate.Fill)
        {
            return CommandOutcome.Reject("Fill sólo vale en monitores auxiliares: en el principal manda el Centro.");
        }

        if (TemplateOf(c.Display, c.Home) == c.Template)
        {
            return CommandOutcome.Accept();
        }

        _templates[(c.Display, c.Home)] = c.Template;
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private CommandOutcome DoDisplayRemoved(string display)
    {
        // Se recuerda aunque no tenga ventanas: si después se le envía alguna, debe ir al principal.
        bool added = _missingDisplays.Add(display);
        return added && _windows.Any(w => w.Display == display)
            ? CommandOutcome.Accept(new ApplyLayout())
            : CommandOutcome.Accept();
    }

    private CommandOutcome DoDisplayAdded(string display)
    {
        bool removed = _missingDisplays.Remove(display);
        return removed && _windows.Any(w => w.Display == display)
            ? CommandOutcome.Accept(new ApplyLayout())
            : CommandOutcome.Accept();
    }

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

    private CommandOutcome DoMakeActiveInPanel(nint handle)
    {
        var w = Find(handle);
        if (w is not { Placement: Placement.InPanel })
        {
            return CommandOutcome.Accept();
        }

        int first = _windows.FindIndex(x => SamePanel(x, w) && x.Placement == Placement.InPanel && !x.Detached);
        int index = _windows.IndexOf(w);
        if (first < 0 || first == index)
        {
            return CommandOutcome.Accept();
        }

        _windows.RemoveAt(index);
        _windows.Insert(first, w);
        return CommandOutcome.Accept(new ApplyLayout());
    }

    private bool SamePanel(ManagedWindow a, ManagedWindow b) => Effective(a) == Effective(b) && a.Home == b.Home;

    /// <summary>
    /// Si el layout la tiene minimizada sin que esté estacionada: no es la primera de un panel en pila
    /// o Fill, o es de un panel tapado por el Fill de otro en el mismo auxiliar.
    /// </summary>
    public bool IsHiddenByLayout(nint handle)
    {
        var w = Find(handle);
        if (w is not { Placement: Placement.InPanel, Detached: false })
        {
            return false;
        }

        var panel = _windows.Where(x => SamePanel(x, w) && x.Placement == Placement.InPanel && !x.Detached).ToList();
        var template = TemplateOf(Effective(w), w.Home);
        if (template is PanelTemplate.Stack or PanelTemplate.Fill)
        {
            return panel[0].Handle != handle;
        }

        // Otro panel del mismo auxiliar en Fill la tapa.
        var other = w.Home == Home.Work ? Home.Context : Home.Work;
        var display = Effective(w);
        return display is not null
            && TemplateOf(display, other) == PanelTemplate.Fill
            && _windows.Any(x => Effective(x) == display && x.Home == other && x.Placement == Placement.InPanel && !x.Detached);
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
            ExternalChange.Maximized when w.Placement == Placement.InPanel && Effective(w) is { } aux =>
                [new MakeActiveInPanel(handle), new SetTemplate(aux, w.Home, PanelTemplate.Fill)],
            ExternalChange.Maximized => [new Detach(handle)],

            // Des-maximizar la ventana de un panel en Fill devuelve el panel a Tiles.
            ExternalChange.Unmaximized when Effective(w) is { } shown && TemplateOf(shown, w.Home) == PanelTemplate.Fill =>
                [new SetTemplate(shown, w.Home, PanelTemplate.Tiles)],
            ExternalChange.Unmaximized => [new Detach(handle)],

            ExternalChange.MovedOrResized => [new Detach(handle)],
            _ => [],
        };
    }

    private CommandOutcome DoApplyScene(ApplyScene c)
    {
        var entries = c.Entries.DistinctBy(e => e.Handle).ToList();
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
            _windows.Add(new ManagedWindow(e.Handle, e.Label, e.Home, placement, e.Display));
        }

        _centerHistory.RemoveAll(h => !keep.Contains(h) || h == center);
        if (previousCenter is { } prev && prev != center && keep.Contains(prev))
        {
            _centerHistory.Insert(0, prev);
        }

        _templates.Clear();
        foreach (var (display, home, template) in c.Templates)
        {
            if (display is not null || template != PanelTemplate.Fill)
            {
                _templates[(display, home)] = template;
            }
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
