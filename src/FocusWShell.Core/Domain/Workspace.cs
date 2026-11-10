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
public sealed record ManagedWindow(nint Handle, string Label, Home Home, Placement Placement, string? Display = null);

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

/// <summary>Evento externo: se desconectó un monitor auxiliar. Sus ventanas pasan al principal, nunca al Centro.</summary>
public sealed record DisplayRemoved(string Display) : Command;

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
        Center?.Handle,
        InPanel(null, Home.Work))
    {
        ContextTemplate = TemplateOf(null, Home.Context),
        WorkTemplate = TemplateOf(null, Home.Work),
        Aux = _windows
            .Where(w => w.Display is not null && w.Placement == Placement.InPanel)
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
        Release c => DoRelease(c.Handle),
        RestoreAll => DoRestoreAll(),
        WindowClosed c => DoWindowClosed(c.Handle),
        null => throw new ArgumentNullException(nameof(command)),
        _ => throw new ArgumentException($"Comando desconocido: {command.GetType().Name}", nameof(command)),
    };

    private List<nint> InPanel(string? display, Home home) =>
        _windows.Where(w => w.Display == display && w.Home == home && w.Placement == Placement.InPanel).Select(w => w.Handle).ToList();

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
        Replace(w with { Placement = Placement.Center });
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
        var affected = _windows.Where(w => w.Display == display).ToList();
        if (affected.Count == 0)
        {
            return CommandOutcome.Accept();
        }

        // Al final de los paneles de su mismo hogar en el principal. La del Centro, si venía de ahí,
        // sigue en el Centro y volverá al principal.
        foreach (var w in affected)
        {
            _windows.Remove(w);
            _windows.Add(w with { Display = null });
        }

        return CommandOutcome.Accept(new ApplyLayout());
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
