using System.Text.RegularExpressions;

namespace FocusWShell.Core.Browsers;

/// <summary>Título de una pestaña sin lo que el navegador le añade para los lectores de pantalla, y su grupo.</summary>
public sealed record CleanTabTitle(string Title, string? Group);

/// <summary>
/// Limpia el nombre accesible de una pestaña de Chrome o Edge (spike S12). El navegador añade al título,
/// separados por « - », su estado («Sleeping», «Memory usage - 153 MB»…) y su grupo («Part of tab group IA»).
/// Se quitan desde el final mientras sean añadidos conocidos; lo que no se reconoce se deja, para no comerse
/// parte de un título que también usa « - ». Los textos dependen del idioma del navegador: si no se
/// reconocen, el título sale con ellos, pero sale.
/// </summary>
public static partial class TabTitle
{
    private const string Separator = " - ";

    /// <summary>Estados que el navegador añade al final (inglés y español).</summary>
    private static readonly HashSet<string> States = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sleeping", "Network error", "Crashed", "Audio playing", "Audio muted", "Pinned",
        "Recording", "Camera recording", "Microphone recording", "Screen sharing",
        "Bluetooth device connected", "USB device connected", "Serial port connected", "Picture in picture",
        "Inactiva", "Suspendida", "Error de red", "Se ha bloqueado", "Reproduciendo audio", "Audio silenciado", "Fijada",
    };

    [GeneratedRegex(@"^\d+([.,]\d+)?\s?(KB|MB|GB)$", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryAmount();

    [GeneratedRegex(@"^(Part of (tab )?group|Parte del grupo( de pestañas)?)\s+(?<group>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex GroupPart();

    [GeneratedRegex(@"^(High memory usage|Memory usage|Uso de memoria|Uso elevado de memoria)$", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryLabel();

    public static CleanTabTitle Clean(string accessibleName)
    {
        ArgumentNullException.ThrowIfNull(accessibleName);
        var parts = accessibleName.Split(Separator).ToList();
        string? group = null;
        while (parts.Count > 1)
        {
            var last = parts[^1].Trim();
            if (parts.Count > 2 && MemoryAmount().IsMatch(last) && MemoryLabel().IsMatch(parts[^2].Trim()))
            {
                parts.RemoveRange(parts.Count - 2, 2);
            }
            else if (States.Contains(last))
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (GroupPart().Match(last) is { Success: true } match)
            {
                group = match.Groups["group"].Value.Trim();
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                break;
            }
        }

        var title = string.Join(Separator, parts).Trim();
        return new CleanTabTitle(title.Length > 0 ? title : accessibleName.Trim(), group);
    }
}
