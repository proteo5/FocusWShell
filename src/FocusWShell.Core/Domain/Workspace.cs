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
public sealed record ManagedWindow(nint Handle, string Label, Home Home, Placement Placement);

/// <summary>Qué va en cada región, en orden: lo que el motor de layout necesita.</summary>
public sealed record SceneComposition(IReadOnlyList<nint> Context, nint? Center, IReadOnlyList<nint> Work)
{
    public IEnumerable<nint> All => Context.Concat(Center is { } c ? [c] : []).Concat(Work);
}

#pragma warning disable CA1716 // Nombres del modelo de dominio (02-modelo-de-dominio.md).

/// <summary>Comandos del núcleo (02-modelo-de-dominio.md, "Ubicaciones y transiciones").</summary>
public abstract record Command;

/// <summary>Empieza a gestionar una ventana elegible y la pone en su panel.</summary>
public sealed record Adopt(nint Handle, string Label, Home Home) : Command;

/// <summary>Lleva la ventana al Centro; la que estaba vuelve a su hogar. Activa la ventana.</summary>
public sealed record Promote(nint Handle) : Command;

/// <summary>Devuelve la ventana del Centro a su panel. El Centro queda vacío.</summary>
public sealed record ReturnToPanel(nint Handle) : Command;

/// <summary>Promueve la ventana que ocupó el Centro antes (CA2).</summary>
public sealed record SwapWithPrevious : Command;

/// <summary>Cambia el hogar de la ventana. Si estaba en su panel, pasa al otro.</summary>
public sealed record SendTo(nint Handle, Home Home) : Command;

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

    public IReadOnlyList<ManagedWindow> Windows => _windows;

    public IReadOnlyList<nint> CenterHistory => _centerHistory;

    public ManagedWindow? Center => _windows.FirstOrDefault(w => w.Placement == Placement.Center);

    public ManagedWindow? Find(nint handle) => _windows.FirstOrDefault(w => w.Handle == handle);

    public bool IsManaged(nint handle) => Find(handle) is not null;

    public SceneComposition Composition => new(
        InPanel(Home.Context),
        Center?.Handle,
        InPanel(Home.Work));

    public CommandOutcome Execute(Command command) => command switch
    {
        Adopt c => DoAdopt(c),
        Promote c => DoPromote(c.Handle),
        ReturnToPanel c => DoReturnToPanel(c.Handle),
        SwapWithPrevious => DoSwapWithPrevious(),
        SendTo c => DoSendTo(c),
        Release c => DoRelease(c.Handle),
        RestoreAll => DoRestoreAll(),
        WindowClosed c => DoWindowClosed(c.Handle),
        null => throw new ArgumentNullException(nameof(command)),
        _ => throw new ArgumentException($"Comando desconocido: {command.GetType().Name}", nameof(command)),
    };

    private List<nint> InPanel(Home home) =>
        _windows.Where(w => w.Home == home && w.Placement == Placement.InPanel).Select(w => w.Handle).ToList();

    private void Replace(ManagedWindow updated) =>
        _windows[_windows.FindIndex(w => w.Handle == updated.Handle)] = updated;

    private CommandOutcome DoAdopt(Adopt c)
    {
        if (IsManaged(c.Handle))
        {
            return CommandOutcome.Reject($"{c.Label} ya está gestionada.");
        }

        _windows.Add(new ManagedWindow(c.Handle, c.Label, c.Home, Placement.InPanel));
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
