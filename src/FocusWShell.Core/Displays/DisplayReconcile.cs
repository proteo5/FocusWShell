using FocusWShell.Core.Domain;

namespace FocusWShell.Core.Displays;

/// <summary>Qué pasó con los monitores, para contárselo al usuario.</summary>
public abstract record DisplayEvent;

/// <summary>Un auxiliar conocido se desconectó: sus ventanas pasan a los paneles del principal.</summary>
public sealed record DisplayLost(string Display) : DisplayEvent;

/// <summary>Un auxiliar conocido volvió: recupera sus ventanas.</summary>
public sealed record DisplayReturned(string Display) : DisplayEvent;

/// <summary>
/// Cambió el monitor principal (M2): Windows eligió otro, normalmente porque el anterior se desconectó.
/// </summary>
/// <param name="Previous">El principal de antes.</param>
/// <param name="Current">El de ahora.</param>
/// <param name="PreviousConnected">Si el de antes sigue conectado (pasa a ser un auxiliar más).</param>
public sealed record PrincipalChanged(string Previous, string Current, bool PreviousConnected) : DisplayEvent;

/// <summary>
/// Compara los monitores conocidos con los conectados (S6 y M2). El principal es el de Windows
/// (<see cref="DisplayTopology.Principal"/>): si se desconecta, Windows nombra otro y ese pasa a tener el
/// Centro. Si el nuevo principal era un auxiliar con paneles, esos paneles cuentan como desconectados
/// (se juntan con los del principal) hasta que vuelva el principal de antes.
/// </summary>
public static class DisplayReconcile
{
    public sealed record Plan(IReadOnlyList<Command> Commands, IReadOnlyList<DisplayEvent> Events, string? Principal);

    /// <param name="known">Monitores auxiliares que el modelo conoce (tienen paneles).</param>
    /// <param name="isMissing">Si el modelo ya da ese monitor por desconectado.</param>
    /// <param name="displays">Los conectados ahora.</param>
    /// <param name="previousPrincipal">El principal de la última vez; <c>null</c> la primera.</param>
    public static Plan Compute(
        IEnumerable<string> known,
        Func<string, bool> isMissing,
        IReadOnlyList<DisplayInfo> displays,
        string? previousPrincipal)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(isMissing);
        ArgumentNullException.ThrowIfNull(displays);

        var principal = DisplayTopology.Principal(displays);
        var principalKey = principal?.Key;
        var connected = displays.Where(d => d != principal).Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool principalChanged = previousPrincipal is not null && principalKey is not null
            && !string.Equals(previousPrincipal, principalKey, StringComparison.OrdinalIgnoreCase);

        var commands = new List<Command>();
        var events = new List<DisplayEvent>();
        if (principalChanged)
        {
            events.Add(new PrincipalChanged(previousPrincipal!, principalKey!, connected.Contains(previousPrincipal!)));
        }

        foreach (var display in known)
        {
            bool present = connected.Contains(display);
            bool wasMissing = isMissing(display);
            commands.Add(present ? new DisplayAdded(display) : new DisplayRemoved(display));

            // El aviso del cambio de principal ya cuenta lo de estos dos.
            bool coveredByPrincipalChange = principalChanged
                && (string.Equals(display, principalKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(display, previousPrincipal, StringComparison.OrdinalIgnoreCase));
            if (coveredByPrincipalChange)
            {
                continue;
            }

            if (present && wasMissing)
            {
                events.Add(new DisplayReturned(display));
            }
            else if (!present && !wasMissing)
            {
                events.Add(new DisplayLost(display));
            }
        }

        return new Plan(commands, events, principalKey);
    }
}
