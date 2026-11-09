using FocusWShell.Core.Displays;
using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Tests.Displays;

public class DisplayTopologyTests
{
    private static DisplayInfo Display(int left, int top, int width, int height) =>
        new(0, "test", PixelRect.FromSize(left, top, width, height), PixelRect.FromSize(left, top, width, height - 48), false, 96);

    // Configuración real del 2026-11-09: estándar a la derecha del ultrawide.
    private static readonly DisplayInfo Ultrawide = Display(0, 0, 3440, 1440);
    private static readonly DisplayInfo Standard = Display(3440, 0, 1920, 1080);

    [Fact]
    public void UltrawideSharesOnlyItsRightEdge()
    {
        Assert.Equal((false, true), DisplayTopology.SharedEdges(Ultrawide, [Ultrawide, Standard]));
    }

    [Fact]
    public void StandardSharesOnlyItsLeftEdge()
    {
        Assert.Equal((true, false), DisplayTopology.SharedEdges(Standard, [Ultrawide, Standard]));
    }

    [Fact]
    public void PrincipalIsWindowsPrimaryEvenIfNarrower()
    {
        var primaryStandard = Standard with { IsPrimary = true };

        Assert.Equal(primaryStandard, DisplayTopology.Principal([Ultrawide, primaryStandard]));
    }

    [Fact]
    public void PrincipalFallsBackToWidest()
    {
        Assert.Equal(Ultrawide, DisplayTopology.Principal([Standard, Ultrawide]));
        Assert.Null(DisplayTopology.Principal([]));
    }

    [Fact]
    public void SingleMonitorSharesNothing()
    {
        Assert.Equal((false, false), DisplayTopology.SharedEdges(Ultrawide, [Ultrawide]));
    }

    [Fact]
    public void MonitorAboveDoesNotCount()
    {
        var above = Display(0, -1080, 1920, 1080);

        Assert.Equal((false, false), DisplayTopology.SharedEdges(Ultrawide, [Ultrawide, above]));
    }

    [Fact]
    public void NeighbourMustOverlapVertically()
    {
        // Tocan la misma línea vertical pero sólo por una esquina.
        var corner = Display(3440, 1440, 1920, 1080);

        Assert.Equal((false, false), DisplayTopology.SharedEdges(Ultrawide, [Ultrawide, corner]));
    }
}
