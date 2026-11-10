using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Rejilla adaptable de los tiles (ADR 0015).</summary>
public class TileGridTests
{
    // Monitor auxiliar de 1920 px: con celdas de al menos 600 px caben 3 columnas de 640.
    private static readonly PixelRect Aux = new(3440, 0, 5360, 1020);

    private static LayoutWindow Win(int id, TileSize size = TileSize.Small, int minW = 0, int minH = 0) =>
        new(id, $"App{id}", minW == 0 && minH == 0 ? null : new PixelSize(minW, minH)) { TileSize = size };

    private static TilePlacement At(TileArrangement a, int id) => a.Placements.Single(p => p.Window.Handle == id);

    [Fact]
    public void ColumnsComeFromTheWidthAndSizesAreColumns()
    {
        var a = TileGrid.Arrange(Aux, [Win(1, TileSize.Large), Win(2), Win(3, TileSize.Medium)], 0, 600, 0);

        Assert.Equal(3, a.Columns);
        Assert.Equal(new PixelRect(3440, 0, 5360, 510), At(a, 1).Rect);
        Assert.Equal(new PixelRect(3440, 510, 4080, 1020), At(a, 2).Rect);
        Assert.Equal(new PixelRect(4080, 510, 5360, 1020), At(a, 3).Rect);
    }

    [Fact]
    public void ASmallTileFillsAGapLeftEarlier()
    {
        var a = TileGrid.Arrange(Aux, [Win(1, TileSize.Medium), Win(2, TileSize.Medium), Win(3)], 0, 600, 0);

        Assert.Equal((0, 0), (At(a, 1).Row, At(a, 1).Column));
        Assert.Equal((1, 0), (At(a, 2).Row, At(a, 2).Column));
        Assert.Equal((0, 2), (At(a, 3).Row, At(a, 3).Column)); // el hueco de la primera fila
    }

    [Fact]
    public void ATileGrowsUntilItsAppFitsAndIsCutToThePanel()
    {
        var a = TileGrid.Arrange(Aux, [Win(1, minW: 900), Win(2, TileSize.Large)], 0, 600, 0);
        var narrow = TileGrid.Arrange(new PixelRect(0, 0, 619, 1392), [Win(3, TileSize.Large)], 0, 480, 0);

        Assert.Equal(2, At(a, 1).Span);
        Assert.Equal(1, narrow.Columns);
        Assert.Equal(1, At(narrow, 3).Span);
    }

    [Fact]
    public void RowsThatDoNotFitArePagedAndARevealedWindowBringsItsRow()
    {
        var windows = Enumerable.Range(1, 5).Select(i => Win(i)).ToList();

        var top = TileGrid.Arrange(Aux, windows, 0, 2000, 300);
        var revealed = TileGrid.Arrange(Aux, windows, 0, 2000, 300, reveal: 5);

        Assert.Equal(new TilePages(0, 3, 5), top.Pages);
        Assert.Null(At(top, 4).Rect);
        Assert.Equal(new TilePages(2, 3, 5), revealed.Pages);
        Assert.NotNull(At(revealed, 5).Rect);
        Assert.Null(At(revealed, 1).Rect);
    }

    [Fact]
    public void ScrollingPastTheEndShowsTheLastFullPage()
    {
        var windows = Enumerable.Range(1, 5).Select(i => Win(i)).ToList();

        var a = TileGrid.Arrange(Aux, windows, 0, 2000, 300, firstRow: 4);

        Assert.Equal(new TilePages(2, 3, 5), a.Pages);
    }
}
