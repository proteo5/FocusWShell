using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Ventanas de tamaño fijo: se mueven, no se estiran.</summary>
public class FixedSizeTests
{
    private const nint NetBird = 1;
    private const nint Edge = 2;

    [Theory]
    [InlineData(false, AppSizeMode.Auto, true)]
    [InlineData(true, AppSizeMode.Auto, false)]
    [InlineData(true, AppSizeMode.Fixed, true)]
    [InlineData(false, AppSizeMode.Resizable, false)]
    public void WhoIsFixed(bool resizable, AppSizeMode mode, bool expected) =>
        Assert.Equal(expected, FixedSize.IsFixed(resizable, mode));

    [Fact]
    public void NaturalSizeComesFromTheOriginalMinusTheInvisibleBorders()
    {
        // Ya estirada a 943x689 (visible 941x688); su original completo era 368x654.
        var visible = PixelRect.FromSize(10, 10, 941, 688);
        var bounds = PixelRect.FromSize(9, 10, 943, 689);

        Assert.Equal(new PixelSize(366, 653), FixedSize.Natural(visible, bounds, PixelRect.FromSize(100, 100, 368, 654)));
        Assert.Equal(new PixelSize(941, 688), FixedSize.Natural(visible, bounds, null));
    }

    [Fact]
    public void ScalesBetweenMonitors()
    {
        Assert.Equal(new PixelSize(450, 800), FixedSize.Scale(new PixelSize(360, 640), 96, 120));
        Assert.Equal(new PixelSize(360, 640), FixedSize.Scale(new PixelSize(360, 640), 96, 96));
    }

    [Fact]
    public void KeepsItsSizeAtTheTopAndCenteredInItsSlot()
    {
        var slot = PixelRect.FromSize(1000, 100, 900, 1200);
        var (targets, notes) = FixedSize.Fit(
            [new WindowTarget(NetBird, "NetBird", "Trabajo", TargetKind.Visible, slot), new WindowTarget(Edge, "Edge", "Trabajo", TargetKind.Visible, slot)],
            new Dictionary<nint, PixelSize> { [NetBird] = new(366, 653) });

        Assert.Equal(PixelRect.FromSize(1000 + 267, 100, 366, 653), targets[0].Rect);
        Assert.Equal(slot, targets[1].Rect); // las demás, igual
        Assert.Empty(notes);
    }

    [Fact]
    public void MaximizedBecomesCenteredWithoutMaximizing()
    {
        var monitor = PixelRect.FromSize(0, 0, 1920, 1040);
        var (targets, _) = FixedSize.Fit(
            [new WindowTarget(NetBird, "NetBird", "Pantalla completa", TargetKind.Maximized, monitor)],
            new Dictionary<nint, PixelSize> { [NetBird] = new(400, 600) });

        Assert.Equal(TargetKind.Visible, targets[0].Kind);
        Assert.Equal(PixelRect.FromSize(760, 220, 400, 600), targets[0].Rect);
    }

    [Fact]
    public void MinimizedStaysMinimized()
    {
        var t = new WindowTarget(NetBird, "NetBird", "Estacionada", TargetKind.Minimized, default);
        var (targets, _) = FixedSize.Fit([t], new Dictionary<nint, PixelSize> { [NetBird] = new(400, 600) });
        Assert.Equal(t, targets[0]);
    }

    [Fact]
    public void TooBigKeepsItsSizeAtTheCornerAndSaysSo()
    {
        var slot = PixelRect.FromSize(0, 0, 300, 500);
        var (targets, notes) = FixedSize.Fit(
            [new WindowTarget(NetBird, "NetBird", "Contexto", TargetKind.Visible, slot)],
            new Dictionary<nint, PixelSize> { [NetBird] = new(366, 653) });

        Assert.Equal(PixelRect.FromSize(0, 0, 366, 653), targets[0].Rect);
        Assert.Contains("NetBird es de tamaño fijo", Assert.Single(notes));
    }

    [Fact]
    public void CatalogRemembersTheSizeMode()
    {
        var catalog = new AppCatalog();
        catalog.Observe("netbird-ui.exe", "NetBird", null, null, DateTimeOffset.UnixEpoch);

        Assert.True(catalog.SetSizeMode("netbird-ui.exe", AppSizeMode.Fixed));
        Assert.False(catalog.SetSizeMode("netbird-ui.exe", AppSizeMode.Fixed));
        Assert.Equal(AppSizeMode.Fixed, catalog.Find("netbird-ui.exe")!.SizeMode);
    }
}
