using System.Text.RegularExpressions;

namespace FocusWShell.Core.Browsers;

/// <summary>
/// Agrupa páginas por sitio sólo con sus títulos (spike S12): el navegador no expone la dirección de cada
/// pestaña. Un trozo del título cuenta como sitio cuando se repite en varias páginas; lo que no se repite
/// queda suelto, con su propio título. Una regla que se equivoca deja una página suelta o en el grupo de su
/// final, nunca mezcla dos sitios a propósito. Las reglas, en orden:
/// <list type="number">
/// <item>Patrones conocidos: una parte <c>dueño/repo</c> es GitHub; un correo con final «Gmail» o «… Mail», Gmail.</item>
/// <item>El final del título se repite en otra página («… - Jira»).</item>
/// <item>El principio se repite («Pull requests · …»).</item>
/// <item>El título entero es un sitio ya detectado (la portada de YouTube se llama «YouTube»).</item>
/// <item>El final empieza por un sitio ya detectado («OVHcloud Documentation» → OVHcloud).</item>
/// <item>El título es sólo una dirección y su nombre coincide con un sitio («spva.reliantparking.com/…» → Reliant Parking).</item>
/// <item>Una dirección y un final comparten nombre («www.hikvision.com» y «… | Hikvision Global»).</item>
/// </list>
/// Un sitio con una sola página no cuenta: la página queda suelta.
/// </summary>
public static partial class PageSites
{
    private static readonly HashSet<string> IgnoredLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "m", "app", "com", "org", "net", "io", "ai", "co", "mx", "es", "dev", "gov", "edu",
    };

    [GeneratedRegex(@"\s+(?:-|\||·|—|–)\s+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^\(\d+\)\s*")]
    private static partial Regex Counter();

    [GeneratedRegex(@"^[\w-]+(\.[\w-]+)+(/.*)?$")]
    private static partial Regex Address();

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+(:.*)?$")]
    private static partial Regex Repository();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.\w+$")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(Gmail|Mail)$")]
    private static partial Regex MailSuffix();

    [GeneratedRegex(@"[^\p{L}\p{N}]")]
    private static partial Regex NotAlphanumeric();

    /// <summary>El sitio de cada título (en el mismo orden), o <c>null</c> si queda suelto.</summary>
    public static IReadOnlyList<string?> Assign(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        var parts = titles.Select(Parts).ToList();
        var site = new string?[titles.Count];

        // 1. Patrones conocidos.
        for (int i = 0; i < titles.Count; i++)
        {
            var p = parts[i];
            if (p.Any(x => Repository().IsMatch(x) && !IsAddress(x)))
            {
                site[i] = "GitHub";
            }
            else if (p.Count > 1 && p.Any(x => Email().IsMatch(x)) && MailSuffix().IsMatch(p[^1]))
            {
                site[i] = "Gmail";
            }
        }

        // 2 y 3. Final o principio repetido.
        void Repeated(Func<List<string>, string> pick)
        {
            var groups = Enumerable.Range(0, titles.Count)
                .Where(i => site[i] is null && parts[i].Count > 1)
                .GroupBy(i => pick(parts[i]), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2);
            foreach (var group in groups)
            {
                foreach (var i in group)
                {
                    site[i] = pick(parts[group.First()]);
                }
            }
        }

        Repeated(p => p[^1]);
        Repeated(p => p[0]);

        // 4, 5 y 6. Contra los sitios ya detectados.
        var known = site.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < titles.Count; i++)
        {
            if (site[i] is not null)
            {
                continue;
            }

            var p = parts[i];
            site[i] = known.FirstOrDefault(k => p.Count == 1 && k.Equals(p[0], StringComparison.OrdinalIgnoreCase))
                ?? (p.Count > 1 ? known.FirstOrDefault(k => p[^1].StartsWith(k + " ", StringComparison.OrdinalIgnoreCase)) : null)
                ?? (p.Count == 1 && IsAddress(p[0]) ? known.FirstOrDefault(k => Labels(p[0]).Any(l => Same(l, k))) : null);
        }

        // 7. Una dirección y un final que comparten nombre.
        for (int i = 0; i < titles.Count; i++)
        {
            if (site[i] is not null || parts[i].Count != 1 || !IsAddress(parts[i][0]))
            {
                continue;
            }

            for (int j = 0; j < titles.Count; j++)
            {
                if (j != i && site[j] is null && parts[j].Count > 1
                    && parts[j][^1].Split(' ')[0] is var name && Labels(parts[i][0]).Any(l => Same(l, name)))
                {
                    site[i] = site[j] = name;
                }
            }
        }

        // Un sitio con una sola página no es un grupo: queda suelta, y tocarla la abre directo.
        var counts = site.OfType<string>().GroupBy(x => x, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < site.Length; i++)
        {
            if (site[i] is { } single && counts[single] < 2)
            {
                site[i] = null;
            }
        }

        return site;
    }

    /// <summary>
    /// El título sin el nombre de su sitio, para no repetirlo en cada fila («Board - RM board - Jira» →
    /// «Board - RM board»). Si no lo lleva al final ni al principio, o sólo es el sitio, sale tal cual.
    /// </summary>
    public static string ShortTitle(string title, string site)
    {
        ArgumentNullException.ThrowIfNull(title);
        var p = Parts(title);
        if (p.Count > 1 && p[^1].Equals(site, StringComparison.OrdinalIgnoreCase))
        {
            p.RemoveAt(p.Count - 1);
        }
        else if (p.Count > 1 && p[0].Equals(site, StringComparison.OrdinalIgnoreCase))
        {
            p.RemoveAt(0);
        }
        else
        {
            return title;
        }

        return string.Join(" - ", p);
    }

    private static List<string> Parts(string title)
    {
        var clean = Counter().Replace(title.Trim(), "");
        return [.. Separators().Split(clean).Select(x => x.Trim()).Where(x => x.Length > 0)];
    }

    private static bool IsAddress(string text) => !text.Contains(' ', StringComparison.Ordinal) && Address().IsMatch(text);

    /// <summary>Los nombres de una dirección sin los genéricos («spva.reliantparking.com/x» → spva, reliantparking).</summary>
    private static IEnumerable<string> Labels(string address) =>
        address.Split('/')[0].Split('.').Where(l => l.Length > 1 && !IgnoredLabels.Contains(l));

    private static bool Same(string a, string b) =>
        string.Equals(NotAlphanumeric().Replace(a, ""), NotAlphanumeric().Replace(b, ""), StringComparison.OrdinalIgnoreCase);
}
