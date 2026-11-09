using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Core.Windows;

namespace FocusWShell.Core.Tests.Layout;

public class MinSizeCatalogTests
{
    [Fact]
    public void LearnsWidthWhenAppIsWiderThanRequested()
    {
        var catalog = new MinSizeCatalog();

        bool learned = catalog.Observe("slack.exe", new PixelSize(619, 696), new PixelSize(670, 696), 96);

        Assert.True(learned);
        Assert.Equal(new PixelSize(670, 0), catalog.Get("slack.exe", 96));
    }

    [Fact]
    public void ExactPlacementTeachesNothing()
    {
        var catalog = new MinSizeCatalog();

        Assert.False(catalog.Observe("msedge.exe", new PixelSize(1892, 1392), new PixelSize(1893, 1392), 96));
        Assert.Null(catalog.Get("msedge.exe", 96));
    }

    [Fact]
    public void KeepsLargestObservationPerDimension()
    {
        var catalog = new MinSizeCatalog();
        catalog.Observe("githubdesktop.exe", new PixelSize(929, 464), new PixelSize(962, 661), 96);

        bool learned = catalog.Observe("githubdesktop.exe", new PixelSize(500, 1000), new PixelSize(950, 1000), 96);

        Assert.False(learned);
        Assert.Equal(new PixelSize(962, 661), catalog.Get("githubdesktop.exe", 96));
    }

    [Fact]
    public void ScalesBetweenDpis()
    {
        var catalog = new MinSizeCatalog();
        catalog.Observe("slack.exe", new PixelSize(600, 700), new PixelSize(1005, 700), 144); // 150 %

        Assert.Equal(new PixelSize(670, 0), catalog.Get("slack.exe", 96));
        Assert.Equal(new PixelSize(1005, 0), catalog.Get("slack.exe", 144));
    }

    [Fact]
    public void KeyPrefersAppUserModelId()
    {
        var w = new WindowSnapshot { Handle = 1, Title = "t", ClassName = "c", ProcessId = 1, ProcessName = "Chrome.exe" };

        Assert.Equal("chrome.exe", MinSizeCatalog.KeyFor(w));
        Assert.Equal("Chrome.UserData.Profile1", MinSizeCatalog.KeyFor(w with { AppUserModelId = "Chrome.UserData.Profile1" }));
    }
}
