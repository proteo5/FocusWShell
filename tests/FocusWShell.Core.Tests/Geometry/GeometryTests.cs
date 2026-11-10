using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Geometry;

public class FrameMarginsTests
{
    [Fact]
    public void BetweenMeasuresInvisibleBorders()
    {
        var bounds = new PixelRect(93, 100, 1307, 907);
        var visible = new PixelRect(100, 100, 1300, 900);

        var margins = FrameMargins.Between(bounds, visible);

        Assert.Equal(new FrameMargins(7, 0, 7, 7), margins);
    }

    [Fact]
    public void ExpandIsInverseOfBetween()
    {
        var margins = new FrameMargins(7, 0, 7, 7);
        var target = new PixelRect(620, 0, 2512, 1392);

        var bounds = margins.Expand(target);

        Assert.Equal(target, new PixelRect(
            bounds.Left + margins.Left,
            bounds.Top + margins.Top,
            bounds.Right - margins.Right,
            bounds.Bottom - margins.Bottom));
        Assert.Equal(margins, FrameMargins.Between(bounds, target));
    }
}

public class SplitterTests
{
    private static readonly PixelRect UltrawideWork = new(0, 0, 3440, 1392);

    [Fact]
    public void ColumnsCoverAreaExactlyWithoutGap()
    {
        var cols = Splitter.Columns(UltrawideWork, [18, 55, 27]);

        Assert.Equal(3, cols.Count);
        Assert.Equal(UltrawideWork.Left, cols[0].Left);
        Assert.Equal(cols[0].Right, cols[1].Left);
        Assert.Equal(cols[1].Right, cols[2].Left);
        Assert.Equal(UltrawideWork.Right, cols[2].Right);
        Assert.All(cols, c => Assert.Equal(UltrawideWork.Height, c.Height));
        Assert.Equal(619, cols[0].Width);  // 18 % de 3440 = 619,2
        Assert.Equal(1892, cols[1].Width); // 55 % = 1892
        Assert.Equal(929, cols[2].Width);
    }

    [Fact]
    public void ColumnsRespectGapAndOuterEdges()
    {
        var cols = Splitter.Columns(UltrawideWork, [18, 55, 27], gap: 8);

        Assert.Equal(UltrawideWork.Left, cols[0].Left);
        Assert.Equal(8, cols[1].Left - cols[0].Right);
        Assert.Equal(8, cols[2].Left - cols[1].Right);
        Assert.Equal(UltrawideWork.Right, cols[2].Right);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void EqualRowsCoverAreaExactly(int count)
    {
        var area = new PixelRect(2512, 0, 3440, 1392);

        var rows = Splitter.EqualRows(area, count, gap: 6);

        Assert.Equal(count, rows.Count);
        Assert.Equal(area.Top, rows[0].Top);
        Assert.Equal(area.Bottom, rows[^1].Bottom);
        Assert.All(rows, r => Assert.Equal(area.Width, r.Width));
        Assert.True(rows.Max(r => r.Height) - rows.Min(r => r.Height) <= 1);
    }

    [Fact]
    public void RejectsNonPositiveWeights() =>
        Assert.Throws<ArgumentException>(() => Splitter.Columns(UltrawideWork, [1, 0]));

    [Fact]
    public void RejectsAreaTooSmall() =>
        Assert.Throws<ArgumentException>(() => Splitter.Columns(new PixelRect(0, 0, 10, 10), [1, 1], gap: 9));
}
