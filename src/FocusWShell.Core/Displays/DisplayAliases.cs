namespace FocusWShell.Core.Displays;

/// <summary>
/// Reconoce un monitor conocido que volvió con otra clave. La clave es la ruta del dispositivo
/// (<c>\\?\DISPLAY#SMKD1CE#1&amp;28a6823a&amp;0&amp;UID256#{…}</c>), y su número de instancia puede cambiar: los
/// monitores virtuales (Apollo, la tablet) estrenan uno en cada conexión, y Windows puede renumerar un monitor
/// físico al cambiarlo de puerto. Sin esto, sus paneles se quedan esperando a un monitor que ya no volverá.
/// </summary>
public static class DisplayAliases
{
    /// <summary>
    /// Para cada monitor conectado con una clave nueva, la clave conocida que le corresponde: el mismo modelo, y
    /// uno solo de cada lado (una clave nueva y una conocida ausente de ese modelo). Si hay dos monitores iguales
    /// en juego, no se adivina.
    /// </summary>
    /// <param name="present">Las claves de los monitores conectados ahora.</param>
    /// <param name="known">Las claves que recuerda FocusWShell (sus paneles, el deck).</param>
    /// <returns>Clave nueva → clave conocida.</returns>
    public static IReadOnlyDictionary<string, string> Resolve(IReadOnlyCollection<string> present, IReadOnlyCollection<string> known)
    {
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(known);

        var comparer = StringComparer.OrdinalIgnoreCase;
        var knownSet = known.ToHashSet(comparer);
        var presentSet = present.ToHashSet(comparer);
        var fresh = presentSet.Where(k => !knownSet.Contains(k) && Model(k) is not null).ToList();
        var absent = knownSet.Where(k => !presentSet.Contains(k) && Model(k) is not null).ToList();

        var aliases = new Dictionary<string, string>(comparer);
        foreach (var group in fresh.GroupBy(k => Model(k)!, comparer))
        {
            var candidates = absent.Where(k => comparer.Equals(Model(k), group.Key)).ToList();
            if (group.Count() == 1 && candidates.Count == 1)
            {
                aliases[group.Single()] = candidates[0];
            }
        }

        return aliases;
    }

    /// <summary>El modelo del monitor (<c>SMKD1CE</c>), o <c>null</c> si la clave no es una ruta de dispositivo.</summary>
    public static string? Model(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var parts = key.Split('#');
        return parts.Length >= 3 && parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) && parts[1].Length > 0
            ? parts[1]
            : null;
    }
}
