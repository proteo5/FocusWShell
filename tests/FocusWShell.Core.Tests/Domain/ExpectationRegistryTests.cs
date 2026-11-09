using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Tests.Domain;

public class ExpectationRegistryTests
{
    private const nint Edge = 7;
    private static readonly PixelRect Center = new(670, 0, 2478, 1392);

    private static ObservedWindow At(PixelRect rect) => new(Edge, true, false, false, rect);

    private static ExpectationRegistry WithEdgeInCenter()
    {
        var r = new ExpectationRegistry();
        r.Expect(Edge, new ExpectVisible(Center));
        return r;
    }

    [Fact]
    public void OwnPlacementIsNotAChange()
    {
        Assert.Equal(ExternalChange.None, WithEdgeInCenter().Reconcile(At(Center)));
    }

    [Fact]
    public void OnePixelIsWithinTolerance()
    {
        var off = Center with { Left = Center.Left - 1, Right = Center.Right + 1 };

        Assert.Equal(ExternalChange.None, WithEdgeInCenter().Reconcile(At(off)));
    }

    [Fact]
    public void DraggedWindowIsMoved()
    {
        var moved = Center with { Left = Center.Left + 40, Right = Center.Right + 40 };

        Assert.Equal(ExternalChange.MovedOrResized, WithEdgeInCenter().Reconcile(At(moved)));
    }

    [Fact]
    public void ChangeIsReportedOnlyOnce()
    {
        var r = WithEdgeInCenter();
        var moved = Center with { Top = 100 };

        Assert.Equal(ExternalChange.MovedOrResized, r.Reconcile(At(moved)));
        Assert.Equal(ExternalChange.None, r.Reconcile(At(moved)));
    }

    [Fact]
    public void MinimizeAndMaximizeAreRecognised()
    {
        Assert.Equal(ExternalChange.Minimized, WithEdgeInCenter().Reconcile(new ObservedWindow(Edge, true, true, false, default)));
        Assert.Equal(ExternalChange.Maximized, WithEdgeInCenter().Reconcile(new ObservedWindow(Edge, true, false, true, new PixelRect(0, 0, 3440, 1392))));
    }

    [Fact]
    public void HiddenStackWindowShownByUser()
    {
        var r = new ExpectationRegistry();
        r.Expect(Edge, new ExpectMinimized());

        Assert.Equal(ExternalChange.None, r.Reconcile(new ObservedWindow(Edge, true, true, false, default)));
        Assert.Equal(ExternalChange.Shown, r.Reconcile(At(Center)));
    }

    [Fact]
    public void ClosedWindowIsForgotten()
    {
        var r = WithEdgeInCenter();

        Assert.Equal(ExternalChange.Closed, r.Reconcile(new ObservedWindow(Edge, false, false, false, default)));
        Assert.False(r.IsWatched(Edge));
    }

    [Fact]
    public void UnknownWindowsAreIgnored()
    {
        Assert.Equal(ExternalChange.None, new ExpectationRegistry().Reconcile(new ObservedWindow(99, false, false, false, default)));
    }
}
