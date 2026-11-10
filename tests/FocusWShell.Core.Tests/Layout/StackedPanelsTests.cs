using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Dos paneles en una columna, uno encima del otro (ADR 0011).</summary>
public class StackedPanelsTests
{
    // Área de trabajo del ultrawide medida en S2; franja de pestañas de 40 px.
    private static readonly PixelRect Work = new(0, 0, 3440, 1392);
    private const int Inset = 40;

    private static LayoutWindow Win(int id, string label, int minW = 0, int minH = 0) =>
        new(id, label, minW == 0 && minH == 0 ? null : new PixelSize(minW, minH));

    private static SceneLayoutResult Principal(
        IReadOnlyList<LayoutWindow> upper, IReadOnlyList<LayoutWindow> lower, IReadOnlyList<LayoutWindow>? work = null) =>
        SceneLayoutEngine.Compute(new SceneInput(
            Work,
            new PanelInput("comunicacion", PanelSide.Left, 18, PanelTemplate.Tiles, upper) { Title = "Comunicación" },
            55,
            Win(9, "IDE"),
            new PanelInput("trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, work ?? []),
            PanelTopInset: Inset)
        {
            LeftLower = new PanelInput("notas", PanelSide.Left, 18, PanelTemplate.Tiles, lower) { Title = "Notas", Slot = PanelSlot.Lower },
        });

    private static WindowTarget Target(SceneLayoutResult r, int id) => r.Targets.Single(t => t.Handle == id);

    [Fact]
    public void TwoPanelsShareTheColumnHeightEachWithItsStrip()
    {
        var r = Principal([Win(1, "Slack")], [Win(2, "OneNote")]);

        Assert.Equal(new PixelRect(0, 0, 619, 696), r.Bands["comunicacion"]);
        Assert.Equal(new PixelRect(0, 696, 619, 1392), r.Bands["notas"]);
        Assert.Equal(new PixelRect(0, Inset, 619, 696), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(0, 696 + Inset, 619, 1392), Target(r, 2).Rect);
        Assert.Equal(Inset, r.StripHeight);
        Assert.Equal("notas", Target(r, 2).Region);
    }

    [Fact]
    public void AnEmptyPanelLeavesTheWholeColumnToTheOther()
    {
        var r = Principal([], [Win(2, "OneNote")]);

        Assert.Equal(new PixelRect(0, 0, 619, 1392), r.Bands["notas"]);
        Assert.Equal(new PixelRect(0, Inset, 619, 1392), Target(r, 2).Rect);
        Assert.False(r.Bands.ContainsKey("comunicacion"));
    }

    [Fact]
    public void AWindowTallerThanItsBandLeavesTheLayoutInsteadOfCoveringTheOtherPanel()
    {
        // Ni llevando el reparto al límite cabe: 1300 px más las dos franjas y el mínimo del otro.
        var r = Principal([Win(1, "Teams", minH: 1300)], [Win(2, "OneNote")]);

        Assert.Equal(TargetKind.Minimized, Target(r, 1).Kind);
        Assert.Contains(r.Notes, n => n.Contains("Teams", StringComparison.Ordinal) && n.Contains("comparte la columna", StringComparison.Ordinal));
        Assert.Equal(new PixelRect(0, 696 + Inset, 619, 1392), Target(r, 2).Rect);
    }

    [Fact]
    public void TheColumnIsAsWideAsItsWidestPanelNeeds()
    {
        var r = Principal([Win(1, "Slack")], [Win(2, "Docker", minW: 900)]);

        // El Centro cede hasta su piso (la mitad del ancho): 172 px.
        Assert.Equal(791, r.LeftColumn.Width);
        Assert.Equal(new PixelRect(0, Inset, 791, 696), Target(r, 1).Rect);
        Assert.Contains(r.Notes, n => n.StartsWith("Notas se amplió", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePanelsNeverOverlap()
    {
        var r = Principal([Win(1, "Slack"), Win(3, "Outlook")], [Win(2, "OneNote"), Win(4, "Spotify")], [Win(5, "Edge")]);

        var visible = r.Targets.Where(t => t.Kind == TargetKind.Visible).ToList();
        foreach (var a in visible)
        {
            foreach (var b in visible.Where(b => b.Handle != a.Handle))
            {
                Assert.True(a.Rect.Intersect(b.Rect).IsEmpty, $"{a.Label} {a.Rect} se superpone a {b.Label} {b.Rect}");
            }
        }
    }

    [Fact]
    public void AuxiliaryColumnsAlsoStackTwoPanels()
    {
        var area = new PixelRect(3440, 0, 5360, 1020);
        var r = AuxLayoutEngine.Compute(new AuxInput("aux", area,
        [
            new PanelInput("grafana", PanelSide.Left, 1, PanelTemplate.Tiles, [Win(1, "Grafana")]),
            new PanelInput("logs", PanelSide.Left, 1, PanelTemplate.Tiles, [Win(2, "Logs")]) { Slot = PanelSlot.Lower },
            new PanelInput("chat", PanelSide.Right, 1, PanelTemplate.Tiles, [Win(3, "Chat")]),
        ], TopInset: Inset));

        Assert.Equal(new PixelRect(3440, 0, 4400, 510), r.Bands["grafana"]);
        Assert.Equal(new PixelRect(3440, 510, 4400, 1020), r.Bands["logs"]);
        Assert.Equal(new PixelRect(4400, 0, 5360, 1020), r.Bands["chat"]);
        Assert.Equal(new PixelRect(3440, 510 + Inset, 4400, 1020), r.Targets.Single(t => t.Handle == 2).Rect);
        Assert.Equal(new PixelRect(3440, 510 + Inset, 4400, 1020), r.Columns["logs"]);
    }

    private static readonly PixelRect UpperBand = new(0, 0, 619, 696);
    private static readonly PixelRect LowerBand = new(0, 696, 619, 1392);

    [Fact]
    public void DraggingTheUpperWindowsBottomEdgeMovesTheSplit()
    {
        var heights = StackSeparator.Detect(new(0, Inset, 619, 696), new(0, Inset, 619, 900), UpperBand, LowerBand, Inset);

        Assert.Equal((900, 492), heights);
    }

    [Fact]
    public void DraggingTheLowerWindowsTopEdgeMovesTheSplitAboveItsStrip()
    {
        var heights = StackSeparator.Detect(new(0, 696 + Inset, 619, 1392), new(0, 500, 619, 1392), UpperBand, LowerBand, Inset);

        Assert.Equal((460, 932), heights);
    }

    [Fact]
    public void TheSplitNeverLeavesABandWithoutRoom()
    {
        var heights = StackSeparator.Detect(new(0, Inset, 619, 696), new(0, Inset, 619, 1385), UpperBand, LowerBand, Inset);

        Assert.Equal(1392 - (Inset + StackSeparator.MinWindowHeight), heights!.Value.Upper);
    }

    [Theory]
    [InlineData(0, 0, 619, 800)] // también se movió el borde de arriba (la franja)
    [InlineData(0, Inset, 700, 900)] // también el de la derecha
    [InlineData(0, 100, 619, 756)] // movida entera
    public void AnythingElseIsNotTheStackSeparator(int left, int top, int right, int bottom)
    {
        Assert.Null(StackSeparator.Detect(new(0, Inset, 619, 696), new(left, top, right, bottom), UpperBand, LowerBand, Inset));
    }

    [Fact]
    public void ThePanelsHeightsSetTheSplit()
    {
        var r = SceneLayoutEngine.Compute(new SceneInput(
            Work,
            new PanelInput("comunicacion", PanelSide.Left, 18, PanelTemplate.Tiles, [Win(1, "Slack")]) { Height = 900 },
            55,
            null,
            new PanelInput("trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, []),
            PanelTopInset: Inset)
        {
            LeftLower = new PanelInput("notas", PanelSide.Left, 18, PanelTemplate.Tiles, [Win(2, "OneNote")]) { Slot = PanelSlot.Lower, Height = 492 },
        });

        Assert.Equal(new PixelRect(0, 0, 619, 900), r.Bands["comunicacion"]);
        Assert.Equal(new PixelRect(0, 900, 619, 1392), r.Bands["notas"]);
    }

    [Fact]
    public void ASplitThatLeavesAWindowWithoutRoomIsTakenToTheLimit()
    {
        var r = SceneLayoutEngine.Compute(new SceneInput(
            Work,
            new PanelInput("comunicacion", PanelSide.Left, 18, PanelTemplate.Tiles, [Win(1, "Slack")]) { Height = 1100 },
            55,
            null,
            new PanelInput("trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, []),
            PanelTopInset: Inset)
        {
            LeftLower = new PanelInput("notas", PanelSide.Left, 18, PanelTemplate.Tiles, [Win(2, "Paint", minH: 600)]) { Title = "Notas", Slot = PanelSlot.Lower, Height = 292 },
        });

        Assert.Equal(TargetKind.Visible, Target(r, 2).Kind);
        Assert.Equal(new PixelRect(0, 1392 - 640, 619, 1392), r.Bands["notas"]);
        Assert.Contains(r.Notes, n => n.StartsWith("Notas necesita 640 px", StringComparison.Ordinal));
    }
}
