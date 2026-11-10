using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusWShell.Core.Domain;

namespace FocusWShell.App;

/// <summary>
/// Iconos de las apps del catálogo (ADR 0011), sacados de su ejecutable. No se guardan en disco: se
/// calculan al mostrarlos y se recuerdan mientras dure la sesión. Las apps sin ejecutable legible (algunas
/// de la Store) se quedan sin icono.
/// </summary>
public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sólo en el hilo de la UI.</summary>
    public static ImageSource? Get(CatalogApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (Cache.TryGetValue(app.AppKey, out var cached))
        {
            return cached;
        }

        ImageSource? icon = null;
        if (app.ExecutablePath is { } path && File.Exists(path))
        {
            try
            {
                using var extracted = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(extracted.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                    source.Freeze();
                    icon = source;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                // Sin icono: la fila se muestra igual.
            }
        }

        Cache[app.AppKey] = icon;
        return icon;
    }
}
