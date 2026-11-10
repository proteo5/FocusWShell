using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>
/// Mover el separador entre dos paneles apilados en una columna (ADR 0011) redimensionando una ventana
/// por el borde que da al otro panel: la de arriba por abajo o la de abajo por arriba.
/// </summary>
public static class StackSeparator
{
    /// <summary>Alto mínimo para las ventanas de una banda al arrastrar (además de su franja).</summary>
    public const int MinWindowHeight = 80;

    /// <summary>
    /// Si la ventana pasó de <paramref name="expected"/> a <paramref name="observed"/> moviendo sólo el
    /// borde que da al otro panel de su columna, devuelve el alto nuevo de cada banda (arriba y abajo,
    /// con sus franjas); si no, <c>null</c>.
    /// </summary>
    /// <param name="upper">Banda del panel de arriba, con su franja.</param>
    /// <param name="lower">Banda del panel de abajo, con su franja.</param>
    /// <param name="stripHeight">Alto de la franja de pestañas de cada banda.</param>
    public static (int Upper, int Lower)? Detect(
        PixelRect expected, PixelRect observed, PixelRect upper, PixelRect lower, int stripHeight, int gap = 0)
    {
        int tol = ColumnSeparator.Tolerance;
        bool Same(int a, int b) => Math.Abs(a - b) <= tol;
        if (!Same(expected.Left, observed.Left) || !Same(expected.Right, observed.Right))
        {
            return null;
        }

        bool topMoved = !Same(expected.Top, observed.Top);
        bool bottomMoved = !Same(expected.Bottom, observed.Bottom);
        if (topMoved == bottomMoved)
        {
            return null;
        }

        int minBand = stripHeight + MinWindowHeight;
        int boundary; // borde de abajo de la banda de arriba
        if (bottomMoved && Same(expected.Bottom, upper.Bottom) && upper.Contains(expected.Left + (expected.Width / 2), expected.Top))
        {
            boundary = observed.Bottom;
        }
        else if (topMoved && Same(expected.Top, lower.Top + stripHeight) && lower.Contains(expected.Left + (expected.Width / 2), expected.Bottom - 1))
        {
            // La ventana de abajo empieza bajo su franja: la banda empieza una franja más arriba.
            boundary = observed.Top - stripHeight - gap;
        }
        else
        {
            return null;
        }

        boundary = Math.Clamp(boundary, upper.Top + minBand, lower.Bottom - gap - minBand);
        return (boundary - upper.Top, lower.Bottom - (boundary + gap));
    }
}
