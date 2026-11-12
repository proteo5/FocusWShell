using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>
/// Mover el separador entre dos columnas del principal redimensionando una ventana del layout por el
/// borde que comparte con la columna vecina (ADR 0012). Cualquier otro cambio no es un separador.
/// </summary>
public static class ColumnSeparator
{
    /// <summary>Holgura por borde: lo que puede variar un borde "quieto".</summary>
    public const int Tolerance = 2;

    /// <summary>Ninguna columna baja de esto al arrastrar; el motor aplica después los mínimos reales.</summary>
    public const int MinColumnWidth = 40;

    /// <summary>
    /// Si la ventana pasó de <paramref name="expected"/> a <paramref name="observed"/> moviendo sólo el
    /// borde que comparte con la columna vecina, devuelve los pesos nuevos (los anchos resultantes, en
    /// píxeles); si no, <c>null</c>.
    /// </summary>
    public static ViewWeights? Detect(PixelRect expected, PixelRect observed, SceneLayoutResult layout, int gap = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        bool Same(int a, int b) => Math.Abs(a - b) <= Tolerance;
        if (!Same(expected.Top, observed.Top) || !Same(expected.Bottom, observed.Bottom))
        {
            return null;
        }

        bool leftMoved = !Same(expected.Left, observed.Left);
        bool rightMoved = !Same(expected.Right, observed.Right);
        if (leftMoved == rightMoved)
        {
            return null; // nada cambió en horizontal, o se movió entera
        }

        // Columnas en el orden en que se ven, con su identidad.
        var columns = new List<(char Id, PixelRect Rect)>
        {
            ('L', layout.LeftColumn),
            ('C', layout.CenterColumn),
            ('R', layout.RightColumn),
        };
        // Una columna apagada (ancho cero) no es vecina de nadie (ADR 0012, enmienda).
        columns.RemoveAll(c => c.Rect.Width <= 0);
        columns.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));

        int centerX = expected.Left + (expected.Width / 2);
        int i = columns.FindIndex(c => c.Rect.Left <= centerX && centerX < c.Rect.Right);
        if (i < 0)
        {
            return null;
        }

        var own = columns[i].Rect;
        int n = rightMoved ? i + 1 : i - 1;
        if (n < 0 || n >= columns.Count)
        {
            return null; // es el borde del monitor
        }

        // La ventana tenía que estar alineada con el borde de su columna (no anclada hacia fuera).
        if (rightMoved ? !Same(expected.Right, own.Right) : !Same(expected.Left, own.Left))
        {
            return null;
        }

        var widths = columns.Select(c => c.Rect.Width).ToArray();
        var neighbor = columns[n].Rect;
        if (rightMoved)
        {
            int boundary = Math.Clamp(observed.Right, own.Left + MinColumnWidth, neighbor.Right - gap - MinColumnWidth);
            widths[i] = boundary - own.Left;
            widths[n] = neighbor.Right - (boundary + gap);
        }
        else
        {
            int boundary = Math.Clamp(observed.Left, neighbor.Left + gap + MinColumnWidth, own.Right - MinColumnWidth);
            widths[i] = own.Right - boundary;
            widths[n] = boundary - gap - neighbor.Left;
        }

        // La apagada conserva un peso positivo cualquiera: no se usa en esta disposición.
        double Width(char id) => columns.FindIndex(c => c.Id == id) is var k and >= 0 ? widths[k] : 1;
        return new ViewWeights(Width('L'), Width('C'), Width('R'));
    }
}
