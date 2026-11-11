using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusWShell.Core.Domain;
using FocusWShell.Win32;

namespace FocusWShell.App;

/// <summary>
/// Iconos de las apps del catálogo (ADR 0011). No se guardan en disco: se calculan al mostrarlos y se
/// recuerdan mientras dure la sesión, por app y origen (si cambia la ruta, se vuelve a intentar).
/// <list type="number">
/// <item>Apps empaquetadas con AUMID (Calculadora, Configuración, Edge): el icono del shell. Por su ejecutable
/// saldrían con el de <c>ApplicationFrameHost</c>.</item>
/// <item>El icono del ejecutable.</item>
/// <item>Si no se puede leer el ejecutable (p. ej. dentro de <c>WindowsApps</c>), el icono que da el shell para esa ruta.</item>
/// </list>
/// </summary>
public static class AppIcons
{
    private const int Size = 48;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sólo en el hilo de la UI.</summary>
    public static ImageSource? Get(CatalogApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var key = $"{app.AppKey}|{app.Aumid}|{app.ExecutablePath}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Si la ruta quedó vieja (la app se actualizó estando cerrada), la de la versión nueva.
        var path = app.ExecutablePath is { } stored ? ExecutableLocator.Resolve(stored) ?? stored : null;
        var icon = (app.Aumid is { } aumid ? FromShell(ShellIcon.ForAumid(aumid, Size)) : null)
            ?? (path is not null ? FromExecutable(path) ?? FromShell(ShellIcon.For(path, Size)) : null)
            ?? (ExecutablePaths.GuessPackagedAumid(path) is { } guessed ? FromShell(ShellIcon.ForAumid(guessed, Size)) : null);
        Cache[key] = icon;
        return icon;
    }

    private static BitmapSource? FromShell(IconPixels? pixels)
    {
        if (pixels is null)
        {
            return null;
        }

        var source = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null, pixels.Bgra, pixels.Width * 4);
        source.Freeze();
        return source;
    }

    private static BitmapSource? FromExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var extracted = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (extracted is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(extracted.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(Size, Size));
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null; // sin icono: la fila se muestra igual
        }
    }
}
