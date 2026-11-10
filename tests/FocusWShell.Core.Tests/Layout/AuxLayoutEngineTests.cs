using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

public class AuxLayoutEngineTests
{
    // Monitor estándar al 125 %, medido el 2026-11-09.
    private static readonly PixelRect Area = new(3440, 0, 5360, 1020);

    private static LayoutWindow Win(int id, string label, int minW = 0, int minH = 0) =>
        new(id, label, minW == 0 && minH == 0 ? null : new PixelSize(minW, minH));

    private static PanelInput Panel(string name, PanelTemplate template, params LayoutWindow[] windows) =>
        new(name, PanelSide.Left, 1, template, windows);

    /// <summary>El primer panel va a la izquierda y el resto a la derecha: cada uno en su columna.</summary>
    private static AuxLayoutResult Compute(params PanelInput[] panels) =>
        AuxLayoutEngine.Compute(new AuxInput("aux", Area, [.. panels.Select((p, i) => p with { Side = i == 0 ? PanelSide.Left : PanelSide.Right })]));

    private static WindowTarget Target(AuxLayoutResult r, int id) => r.Targets.Single(t => t.Handle == id);

    /// <summary>En un auxiliar nada sale del monitor.</summary>
    private static void AssertInsideMonitor(AuxLayoutResult r)
    {
        foreach (var t in r.Targets.Where(t => t.Kind != TargetKind.Minimized))
        {
            Assert.True(t.Rect.Left >= Area.Left && t.Rect.Right <= Area.Right && t.Rect.Top >= Area.Top && t.Rect.Bottom <= Area.Bottom,
                $"{t.Label} sale del monitor: {t.Rect}");
        }
    }

    [Fact]
    public void SinglePanelTakesTheWholeMonitor()
    {
        var r = Compute(Panel("Trabajo", PanelTemplate.Tiles, Win(1, "Logs"), Win(2, "Grafana")));

        Assert.Equal(new PixelRect(3440, 0, 5360, 510), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(3440, 510, 5360, 1020), Target(r, 2).Rect);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void TopInsetAppliesToPanelsButNotToFill()
    {
        var tiles = AuxLayoutEngine.Compute(new AuxInput("aux", Area, [Panel("Trabajo", PanelTemplate.Tiles, Win(1, "Logs"))], TopInset: 35));
        var fill = AuxLayoutEngine.Compute(new AuxInput("aux", Area, [Panel("Trabajo", PanelTemplate.Fill, Win(1, "Logs"))], TopInset: 35));

        Assert.Equal(new PixelRect(3440, 35, 5360, 1020), Target(tiles, 1).Rect);
        Assert.Equal(Area, Target(fill, 1).Rect);
    }

    [Fact]
    public void EmptyPanelsTakeNoSpace()
    {
        var r = Compute(Panel("Contexto", PanelTemplate.Tiles), Panel("Trabajo", PanelTemplate.Tiles, Win(1, "Logs")));

        Assert.Equal(Area, Target(r, 1).Rect);
    }

    [Fact]
    public void TwoPanelsSplitByWeight()
    {
        var r = Compute(Panel("Contexto", PanelTemplate.Tiles, Win(1, "Chat")), Panel("Trabajo", PanelTemplate.Tiles, Win(2, "Logs")));

        Assert.Equal(new PixelRect(3440, 0, 4400, 1020), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(4400, 0, 5360, 1020), Target(r, 2).Rect);
    }

    [Fact]
    public void FillMaximizesFirstWindowAndHidesEverythingElse()
    {
        var r = Compute(
            Panel("Contexto", PanelTemplate.Tiles, Win(1, "Chat")),
            Panel("Trabajo", PanelTemplate.Fill, Win(2, "Grafana"), Win(3, "Logs")));

        Assert.Equal(TargetKind.Maximized, Target(r, 2).Kind);
        Assert.Equal(Area, Target(r, 2).Rect);
        Assert.Equal(TargetKind.Minimized, Target(r, 1).Kind);
        Assert.Equal(TargetKind.Minimized, Target(r, 3).Kind);
        Assert.Single(r.Targets, t => t.Kind == TargetKind.Maximized);
        Assert.Contains(r.Notes, n => n.Contains("Grafana", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstFillWins()
    {
        var r = Compute(Panel("Contexto", PanelTemplate.Fill, Win(1, "Chat")), Panel("Trabajo", PanelTemplate.Fill, Win(2, "Logs")));

        Assert.Equal(TargetKind.Maximized, Target(r, 1).Kind);
        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
    }

    [Fact]
    public void StackShowsOnlyTheFirst()
    {
        var r = Compute(Panel("Trabajo", PanelTemplate.Stack, Win(1, "Logs"), Win(2, "Grafana")));

        Assert.Equal(Area, Target(r, 1).Rect);
        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
    }

    [Fact]
    public void MinimumWidthTakesFromTheOtherPanel()
    {
        var r = Compute(
            Panel("Contexto", PanelTemplate.Tiles, Win(1, "WhatsApp", 1200)),
            Panel("Trabajo", PanelTemplate.Tiles, Win(2, "Logs", 500)));

        Assert.Equal(1200, Target(r, 1).Rect.Width);
        Assert.Equal(720, Target(r, 2).Rect.Width);
        Assert.Contains(r.Notes, n => n.Contains("WhatsApp", StringComparison.Ordinal));
        AssertInsideMonitor(r);
    }

    [Fact]
    public void WindowThatCannotFitLeavesTheLayoutInsteadOfOverflowing()
    {
        var r = Compute(
            Panel("Contexto", PanelTemplate.Tiles, Win(1, "Docker", 1500)),
            Panel("Trabajo", PanelTemplate.Tiles, Win(2, "Logs", 600)));

        Assert.Equal(TargetKind.Minimized, Target(r, 1).Kind);
        Assert.Equal(Area, Target(r, 2).Rect);
        AssertInsideMonitor(r);
    }

    [Fact]
    public void TilesTooTallBecomeStack()
    {
        var r = Compute(Panel("Trabajo", PanelTemplate.Tiles, Win(1, "A", 0, 700), Win(2, "B", 0, 700)));

        Assert.Equal(PanelTemplate.Stack, r.EffectiveTemplates["Trabajo"]);
        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
    }
}
