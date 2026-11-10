namespace FocusWShell.Core.Displays;

/// <summary>Relaciones entre monitores que importan al layout.</summary>
public static class DisplayTopology
{
    /// <summary>
    /// Monitor principal de FocusWShell, el único con Centro (ADR 0008): el principal de Windows.
    /// Si Windows no marca ninguno, el más ancho.
    /// </summary>
    public static DisplayInfo? Principal(IEnumerable<DisplayInfo> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        var list = all.ToList();
        return list.FirstOrDefault(d => d.IsPrimary) ?? list.MaxBy(d => d.WorkArea.Width);
    }

    /// <summary>
    /// Si el borde izquierdo o derecho de <paramref name="display"/> limita con otro monitor: comparten
    /// esa línea vertical y se solapan en altura. Un desborde por ese borde caería en el vecino.
    /// </summary>
    public static (bool Left, bool Right) SharedEdges(DisplayInfo display, IEnumerable<DisplayInfo> all)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(all);
        var b = display.Bounds;
        bool left = false, right = false;
        foreach (var other in all)
        {
            var o = other.Bounds;
            if (o == b)
            {
                continue;
            }

            bool overlapsVertically = o.Top < b.Bottom && o.Bottom > b.Top;
            left |= overlapsVertically && o.Right == b.Left;
            right |= overlapsVertically && o.Left == b.Right;
        }

        return (left, right);
    }
}
