using FocusWShell.Core.Geometry;
using FocusWShell.Core.Windows;

namespace FocusWShell.Core.Tests.Windows;

/// <summary>Pantalla completa propia de las apps (M2).</summary>
public class AppFullscreenTests
{
    private static readonly PixelRect Monitor = new(0, 0, 3440, 1440);

    [Fact]
    public void AWindowCoveringTheWholeMonitorIsFullscreen() =>
        Assert.True(AppFullscreen.Covers(new PixelRect(0, 0, 3440, 1440), Monitor, maximized: false));

    [Fact]
    public void AMaximizedWindowIsNotEvenIfItCoversTheMonitor() =>
        Assert.False(AppFullscreen.Covers(new PixelRect(-8, -8, 3448, 1448), Monitor, maximized: true));

    [Fact]
    public void LeavingTheTaskbarVisibleIsNotFullscreen() =>
        Assert.False(AppFullscreen.Covers(new PixelRect(0, 0, 3440, 1392), Monitor, maximized: false));

    [Fact]
    public void ACoupleOfPixelsOffStillCounts() =>
        Assert.True(AppFullscreen.Covers(new PixelRect(1, 1, 3439, 1439), Monitor, maximized: false));
}
