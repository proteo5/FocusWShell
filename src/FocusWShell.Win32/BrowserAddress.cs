using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace FocusWShell.Win32;

/// <summary>Lo que muestra la barra de direcciones de una ventana de navegador.</summary>
/// <param name="Value">El texto de la barra (sin leer si es privada o si se está escribiendo en ella).</param>
/// <param name="Private">Ventana de incógnito (Chrome) o InPrivate (Edge).</param>
/// <param name="Editing">La barra tiene el teclado: lo que muestra es lo que se escribe, no la página.</param>
public sealed record BrowserAddress(string? Value, bool Private, bool Editing);

/// <summary>
/// La barra de direcciones y si la ventana es privada, con UI Automation (ADR 0019, sitios), sin extensión.
/// Las dos están en la interfaz del navegador, fuera de la página: no se entra en el contenido web. Una
/// ventana privada se reconoce por su botón «Incognito» o «InPrivate», cuyo nombre depende del idioma del
/// navegador. Unos 15 a 30 ms por ventana: se llama desde un hilo de fondo, nunca desde el de la UI.
/// </summary>
public static partial class BrowserAddressReader
{
    /// <summary>«Incognito», «Incógnito», «Inkognito», «InPrivate», «Navegación InPrivate»…</summary>
    [GeneratedRegex(@"in[ck]ogn|inprivate", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrivateName();

    /// <summary>La barra de la ventana, o <c>null</c> si no se encontró (una app web, o la ventana ya no está).</summary>
    public static BrowserAddress? Read(nint handle)
    {
        try
        {
            var window = AutomationElement.FromHandle(handle);
            AutomationElement? bar = null;
            // Edge lo dice también en el título; Chrome, sólo con su botón.
            bool isPrivate = (window.Current.Name ?? string.Empty).Contains("InPrivate", StringComparison.Ordinal);
            Walk(window, 0, ref bar, ref isPrivate);
            if (bar is null)
            {
                return isPrivate ? new BrowserAddress(null, true, false) : null;
            }

            if (isPrivate)
            {
                return new BrowserAddress(null, true, false);
            }

            if (bar.Current.HasKeyboardFocus)
            {
                return new BrowserAddress(null, false, true);
            }

            return bar.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                ? new BrowserAddress(((ValuePattern)pattern).Current.Value, false, false)
                : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or TimeoutException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Sólo si la ventana es privada (para la lista de páginas del deck).</summary>
    public static bool IsPrivate(nint handle) => Read(handle)?.Private == true;

    /// <summary>La primera caja de texto fuera de la página es la barra; cualquier botón privado marca la ventana.</summary>
    private static void Walk(AutomationElement element, int depth, ref AutomationElement? bar, ref bool isPrivate)
    {
        if (depth > 12 || (bar is not null && isPrivate))
        {
            return;
        }

        var walker = TreeWalker.ControlViewWalker;
        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            var type = child.Current.ControlType;
            if (type == ControlType.Document)
            {
                continue; // la página: ni su contenido ni sus cajas de texto
            }

            if (type == ControlType.Edit)
            {
                bar ??= child;
                continue;
            }

            if (type == ControlType.Button && PrivateName().IsMatch(child.Current.Name ?? string.Empty))
            {
                isPrivate = true;
                continue;
            }

            Walk(child, depth + 1, ref bar, ref isPrivate);
            if (bar is not null && isPrivate)
            {
                return;
            }
        }
    }
}
