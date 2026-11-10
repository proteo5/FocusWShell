using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Divide un área en columnas o filas según fracciones, sin perder píxeles por redondeo.</summary>
public static class Splitter
{
    /// <summary>Columnas de izquierda a derecha.</summary>
    /// <param name="weights">Pesos relativos; no tienen que sumar 1 (p. ej. 18, 55, 27).</param>
    /// <param name="gap">Separación en píxeles entre columnas (no en los bordes exteriores).</param>
    public static IReadOnlyList<PixelRect> Columns(PixelRect area, IReadOnlyList<double> weights, int gap = 0)
    {
        var spans = Split(area.Left, area.Right, weights, gap);
        return spans.Select(s => new PixelRect(s.Start, area.Top, s.End, area.Bottom)).ToList();
    }

    /// <summary>Filas de arriba abajo.</summary>
    public static IReadOnlyList<PixelRect> Rows(PixelRect area, IReadOnlyList<double> weights, int gap = 0)
    {
        var spans = Split(area.Top, area.Bottom, weights, gap);
        return spans.Select(s => new PixelRect(area.Left, s.Start, area.Right, s.End)).ToList();
    }

    /// <summary>Filas iguales.</summary>
    public static IReadOnlyList<PixelRect> EqualRows(PixelRect area, int count, int gap = 0) =>
        Rows(area, Enumerable.Repeat(1.0, count).ToList(), gap);

    private static List<(int Start, int End)> Split(int start, int end, IReadOnlyList<double> weights, int gap)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            throw new ArgumentException("Se necesita al menos un peso.", nameof(weights));
        }

        if (weights.Any(w => w <= 0 || double.IsNaN(w) || double.IsInfinity(w)))
        {
            throw new ArgumentException("Los pesos deben ser positivos.", nameof(weights));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        int usable = end - start - (gap * (weights.Count - 1));
        if (usable < weights.Count)
        {
            throw new ArgumentException("El área es demasiado pequeña para esa división.");
        }

        double total = weights.Sum();
        var result = new List<(int, int)>(weights.Count);
        double cumulative = 0;
        int cursor = start;
        for (int i = 0; i < weights.Count; i++)
        {
            cumulative += weights[i];
            // Se redondea la posición acumulada, no cada tamaño: así el último borde cae exacto.
            int sizeEnd = start + (int)Math.Round(usable * cumulative / total) + (gap * i);
            result.Add((cursor, sizeEnd));
            cursor = sizeEnd + gap;
        }

        return result;
    }
}
