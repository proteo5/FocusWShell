using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Tamaño de tile de una ventana (ADR 0015): cuántas columnas ocupa, recortado al panel.</summary>
public enum TileSize
{
    Small = 1,
    Medium = 2,
    Large = 3,
}

/// <summary>Qué filas de un panel en tiles se ven: desde <paramref name="First"/>, <paramref name="Visible"/> de <paramref name="Total"/>.</summary>
public sealed record TilePages(int First, int Visible, int Total)
{
    public bool HasMore => Visible < Total;
}

/// <summary>Una ventana colocada por la rejilla; <c>Rect</c> es <c>null</c> si su fila no se ve.</summary>
public sealed record TilePlacement(LayoutWindow Window, int Row, int Column, int Span, PixelRect? Rect);

public sealed record TileArrangement(IReadOnlyList<TilePlacement> Placements, TilePages Pages, int Columns);

/// <summary>
/// Rejilla adaptable de un panel en tiles (ADR 0015):
/// <list type="number">
/// <item>Las columnas salen del ancho: tantas como quepan de <c>minCellWidth</c> (al menos una).</item>
/// <item>Cada ventana ocupa 1, 2 o 3 columnas según su tamaño, recortado a las que hay. Si su app no
/// cabe en ese ancho, el tile crece hasta que quepa o hasta ocupar todas.</item>
/// <item>En el orden del panel, cada tile va al primer hueco donde quepa (como <c>grid-auto-flow: dense</c>).</item>
/// <item>Las filas se reparten el alto sin bajar del mínimo de sus ventanas ni de <c>minRowHeight</c>.
/// Si no caben todas, se ven las que quepan desde la fila pedida y las demás quedan ocultas.</item>
/// </list>
/// </summary>
public static class TileGrid
{
    public static TileArrangement Arrange(
        PixelRect area,
        IReadOnlyList<LayoutWindow> windows,
        int gap,
        int minCellWidth,
        int minRowHeight,
        int firstRow = 0,
        nint? reveal = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        int columns = minCellWidth <= 0 ? 1 : Math.Max(1, (area.Width + gap) / (minCellWidth + gap));
        var cols = Splitter.Columns(area, Enumerable.Repeat(1.0, columns).ToList(), gap);

        int Width(int start, int span) => cols[start + span - 1].Right - cols[start].Left;

        // 1. Ancho de cada tile: su tamaño, recortado, y crecido si su app no cabe.
        var spans = windows.Select(w =>
        {
            int span = Math.Min((int)w.TileSize, columns);
            while (span < columns && (w.MinSize?.Width ?? 0) > Width(0, span))
            {
                span++;
            }

            return span;
        }).ToList();

        // 2. Empaquetado: primer hueco en que quepa, fila a fila.
        var occupied = new List<bool[]>();
        var cells = new List<(int Row, int Column)>();
        for (int i = 0; i < windows.Count; i++)
        {
            int span = spans[i];
            (int Row, int Column)? spot = null;
            for (int r = 0; r < occupied.Count && spot is null; r++)
            {
                for (int c = 0; c + span <= columns; c++)
                {
                    if (Enumerable.Range(c, span).All(k => !occupied[r][k]))
                    {
                        spot = (r, c);
                        break;
                    }
                }
            }

            if (spot is null)
            {
                occupied.Add(new bool[columns]);
                spot = (occupied.Count - 1, 0);
            }

            for (int k = 0; k < span; k++)
            {
                occupied[spot.Value.Row][spot.Value.Column + k] = true;
            }

            cells.Add(spot.Value);
        }

        int total = occupied.Count;
        if (total == 0)
        {
            return new TileArrangement([], new TilePages(0, 0, 0), columns);
        }

        // 3. Alto que necesita cada fila.
        var need = new int[total];
        for (int i = 0; i < windows.Count; i++)
        {
            need[cells[i].Row] = Math.Max(need[cells[i].Row], windows[i].MinSize?.Height ?? 0);
        }

        for (int r = 0; r < total; r++)
        {
            need[r] = Math.Max(need[r], minRowHeight);
        }

        // 4. Filas visibles: desde la pedida (o la de la ventana a mostrar), las que quepan; si sobra
        //    sitio al final, se suben filas de arriba para no dejar el panel medio vacío.
        int first = Math.Clamp(firstRow, 0, total - 1);
        if (reveal is { } handle && windows.ToList().FindIndex(w => w.Handle == handle) is var at and >= 0)
        {
            first = cells[at].Row;
        }

        int Used(int from, int count) => Enumerable.Range(from, count).Sum(r => need[r]) + (gap * (count - 1));
        int visible = 1;
        while (first + visible < total && Used(first, visible + 1) <= area.Height)
        {
            visible++;
        }

        while (first > 0 && Used(first - 1, visible + 1) <= area.Height)
        {
            first--;
            visible++;
        }

        // 5. Rectángulos.
        var rows = SceneLayoutEngine.DistributeRows(area, need.Skip(first).Take(visible).ToList(), gap);
        var placements = new List<TilePlacement>(windows.Count);
        for (int i = 0; i < windows.Count; i++)
        {
            var (row, column) = cells[i];
            PixelRect? rect = row >= first && row < first + visible
                ? new PixelRect(cols[column].Left, rows[row - first].Top, cols[column + spans[i] - 1].Right, rows[row - first].Bottom)
                : null;
            placements.Add(new TilePlacement(windows[i], row, column, spans[i], rect));
        }

        return new TileArrangement(placements, new TilePages(first, visible, total), columns);
    }
}
