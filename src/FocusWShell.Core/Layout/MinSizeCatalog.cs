using FocusWShell.Core.Geometry;
using FocusWShell.Core.Windows;

namespace FocusWShell.Core.Layout;

/// <summary>
/// Tamaños mínimos aprendidos por app. Windows no permite consultar el mínimo de una ventana de otro
/// proceso, así que se aprende observando: si una app queda más grande de lo pedido, eso es su mínimo.
/// Se guardan en píxeles lógicos (96 DPI) para que sirvan en monitores con otra escala.
/// </summary>
public sealed class MinSizeCatalog
{
    private const int BaseDpi = 96;

    /// <summary>Mínimos por clave de app, en píxeles lógicos. Público para serializar.</summary>
    public Dictionary<string, PixelSize> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clave estable de una app: su AUMID si lo tiene; si no, el nombre del ejecutable.</summary>
    public static string KeyFor(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return !string.IsNullOrEmpty(window.AppUserModelId)
            ? window.AppUserModelId
            : window.ProcessName.ToLowerInvariant();
    }

    /// <summary>Mínimo conocido en píxeles físicos para un DPI dado.</summary>
    public PixelSize? Get(string appKey, int dpi) =>
        Entries.TryGetValue(appKey, out var logical)
            ? new PixelSize(ToPhysical(logical.Width, dpi), ToPhysical(logical.Height, dpi))
            : null;

    /// <summary>
    /// Registra el resultado de una colocación. Devuelve <c>true</c> si se aprendió un mínimo nuevo o mayor.
    /// Sólo cuenta como mínimo una dimensión en la que la app quedó más grande de lo pedido (más de 1 px).
    /// </summary>
    public bool Observe(string appKey, PixelSize requested, PixelSize actual, int dpi)
    {
        bool widthEnforced = actual.Width > requested.Width + 1;
        bool heightEnforced = actual.Height > requested.Height + 1;
        if (!widthEnforced && !heightEnforced)
        {
            return false;
        }

        Entries.TryGetValue(appKey, out var known);
        var observed = new PixelSize(
            widthEnforced ? ToLogical(actual.Width, dpi) : 0,
            heightEnforced ? ToLogical(actual.Height, dpi) : 0);
        var merged = new PixelSize(Math.Max(known.Width, observed.Width), Math.Max(known.Height, observed.Height));
        if (merged == known)
        {
            return false;
        }

        Entries[appKey] = merged;
        return true;
    }

    // Redondeo hacia arriba en ambos sentidos: es preferible sobrestimar un mínimo que quedarse corto.
    private static int ToLogical(int physical, int dpi) => (int)Math.Ceiling(physical * (double)BaseDpi / Normalize(dpi));

    private static int ToPhysical(int logical, int dpi) => (int)Math.Ceiling(logical * (double)Normalize(dpi) / BaseDpi);

    private static int Normalize(int dpi) => dpi > 0 ? dpi : BaseDpi;
}
