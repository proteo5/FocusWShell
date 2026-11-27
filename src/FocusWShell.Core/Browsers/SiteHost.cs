using System.Globalization;

namespace FocusWShell.Core.Browsers;

/// <summary>
/// El sitio de una dirección para la actividad (ADR 0019, sitios): sólo el host, sin <c>www.</c>, en
/// minúsculas. Nunca la ruta, la consulta ni el puerto. Lo que no es una página web (una búsqueda escrita, una
/// página interna del navegador, un archivo local) no tiene sitio.
/// </summary>
public static class SiteHost
{
    /// <summary>El sitio de una ventana privada: se cuenta el tiempo, nunca qué se vio.</summary>
    public const string Private = "(private)";

    /// <summary>El host de lo que muestra la barra de direcciones, o <c>null</c> si no es una página web.</summary>
    /// <remarks>Chrome oculta el <c>https://</c> y Edge lo muestra: se aceptan las dos formas.</remarks>
    public static string? From(string? address)
    {
        var value = address?.Trim();
        if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace))
        {
            return null;
        }

        bool hasScheme = value.Contains("://", StringComparison.Ordinal);
        if (!Uri.TryCreate(hasScheme ? value : "https://" + value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null; // edge://, chrome://, about:, file:// y lo demás
        }

        var host = uri.Host.ToLower(CultureInfo.InvariantCulture);
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || host == "localhost")
        {
            return uri.Host.ToLower(CultureInfo.InvariantCulture);
        }

        // Sin punto, sin esquema, es una palabra suelta que se está buscando («hola»), no un sitio.
        if (!host.Contains('.', StringComparison.Ordinal) || host.StartsWith('.') || host.EndsWith('.'))
        {
            return null;
        }

        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
