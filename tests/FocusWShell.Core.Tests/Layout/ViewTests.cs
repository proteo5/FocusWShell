using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Vistas: el foco a la izquierda, al centro o a la derecha (ADR 0012).</summary>
public class ViewTests
{
    // Área de trabajo del ultrawide medida en S2.
    private static readonly PixelRect Work = new(0, 0, 3440, 1392);

    private static LayoutWindow Win(int id, string label, int minW = 0, int minH = 0) =>
        new(id, label, minW == 0 && minH == 0 ? null : new PixelSize(minW, minH));

    private static SceneLayoutResult Compute(FocusPosition focus, IReadOnlyList<LayoutWindow> context, IReadOnlyList<LayoutWindow> work)
    {
        var (l, c, r) = ViewProportions.Default(focus);
        return SceneLayoutEngine.Compute(new SceneInput(
            Work,
            new PanelInput("contexto", PanelSide.Left, l, PanelTemplate.Tiles, context) { Title = "Contexto" },
            c,
            Win(9, "IDE"),
            new PanelInput("trabajo", PanelSide.Right, r, PanelTemplate.Tiles, work) { Title = "Trabajo" })
        {
            Focus = focus,
        });
    }

    private static WindowTarget Target(SceneLayoutResult r, int id) => r.Targets.Single(t => t.Handle == id);

    [Fact]
    public void WithoutWorkTheFocusTakesItsPlace()
    {
        var r = Compute(FocusPosition.ContextOnly, [Win(1, "Slack")], [Win(2, "Edge")]);

        Assert.Equal(new PixelRect(0, 0, 1204, 1392), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(1204, 0, 3440, 1392), Target(r, 9).Rect);
        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
        Assert.Equal(0, r.RightColumn.Width);
    }

    [Fact]
    public void WithoutContextTheFocusGoesToTheLeftEdge()
    {
        var r = Compute(FocusPosition.WorkOnly, [Win(1, "Slack")], [Win(2, "Edge")]);

        Assert.Equal(new PixelRect(0, 0, 2236, 1392), Target(r, 9).Rect);
        Assert.Equal(new PixelRect(2236, 0, 3440, 1392), Target(r, 2).Rect);
        Assert.Equal(TargetKind.Minimized, Target(r, 1).Kind);
    }

    [Fact]
    public void FocusOnlyFillsTheMonitorAndHidesBothPanels()
    {
        var r = Compute(FocusPosition.FocusOnly, [Win(1, "Slack")], [Win(2, "Edge")]);

        Assert.Equal(Work, Target(r, 9).Rect);
        Assert.Equal(TargetKind.Minimized, Target(r, 1).Kind);
        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
        Assert.Empty(r.Bands);
    }

    [Fact]
    public void TheSeparatorIgnoresAHiddenColumn()
    {
        var r = Compute(FocusPosition.ContextOnly, [Win(1, "Slack")], []);

        // Arrastrar el borde derecho de Slack (el separador con el foco) cambia Contexto y el foco.
        var weights = ColumnSeparator.Detect(new(0, 0, 1204, 1392), new(0, 0, 1404, 1392), r);

        Assert.NotNull(weights);
        Assert.Equal(1404, weights!.Left);
        Assert.Equal(2036, weights.Center);
        Assert.True(weights.IsValid);
    }

    [Fact]
    public void FocusOnTheLeftPutsWorkNextToItAndContextOnTheFarEdge()
    {
        var r = Compute(FocusPosition.Left, [Win(1, "Slack")], [Win(2, "Edge")]);

        Assert.Equal(new PixelRect(0, 0, 1892, 1392), Target(r, 9).Rect);
        Assert.Equal(new PixelRect(1892, 0, 2924, 1392), Target(r, 2).Rect);
        Assert.Equal(new PixelRect(2924, 0, 3440, 1392), Target(r, 1).Rect);
    }

    [Fact]
    public void FocusOnTheRightIsTheMirror()
    {
        var r = Compute(FocusPosition.Right, [Win(1, "Slack")], [Win(2, "Edge")]);

        Assert.Equal(new PixelRect(0, 0, 516, 1392), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(516, 0, 1548, 1392), Target(r, 2).Rect);
        Assert.Equal(new PixelRect(1548, 0, 3440, 1392), Target(r, 9).Rect);
    }

    [Fact]
    public void CenterViewStaysAsBefore()
    {
        var r = Compute(FocusPosition.Center, [Win(1, "Slack")], [Win(2, "Edge")]);

        Assert.Equal(new PixelRect(0, 0, 619, 1392), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(619, 0, 2511, 1392), Target(r, 9).Rect);
        Assert.Equal(new PixelRect(2511, 0, 3440, 1392), Target(r, 2).Rect);
    }

    [Fact]
    public void ThePanelInTheMiddleCannotOverflowSoTheWindowLeavesTheLayout()
    {
        var r = Compute(FocusPosition.Left, [Win(1, "Slack")], [Win(2, "Docker", minW: 1400)]);

        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
        Assert.Contains(r.Notes, n => n.Contains("Docker", StringComparison.Ordinal) && n.Contains("entre el foco", StringComparison.Ordinal));
        AssertNothingOverlaps(r);
    }

    [Fact]
    public void ThePanelOnTheFarEdgeOverflowsOutwardsNeverOverTheOthers()
    {
        var r = Compute(FocusPosition.Left, [Win(1, "Teams", minW: 700)], [Win(2, "Edge")]);

        // Contexto crece 172 px a costa del foco (hasta su piso) y el resto sale por el borde derecho.
        Assert.Equal(new PixelRect(2752, 0, 3452, 1392), Target(r, 1).Rect);
        Assert.Equal(new PixelRect(0, 0, 1720, 1392), Target(r, 9).Rect);
        AssertNothingOverlaps(r);
    }

    private static void AssertNothingOverlaps(SceneLayoutResult r)
    {
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
    public void SetViewOnlyChangesWhereTheFocusIs()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(1, "IDE", Workspace.WorkPanel));
        ws.Execute(new Promote(1));

        var outcome = ws.Execute(new SetView(FocusPosition.Left));

        Assert.True(outcome.Accepted);
        Assert.Equal([new ApplyLayout()], outcome.Effects);
        Assert.Equal(FocusPosition.Left, ws.View);
        Assert.Equal(FocusPosition.Left, ws.Composition.View);
        Assert.Equal((nint)1, ws.Composition.Center);
        Assert.Empty(ws.Execute(new SetView(FocusPosition.Left)).Effects); // misma vista: nada que mover
    }

    [Fact]
    public void AScenesViewIsApplied()
    {
        var ws = new Workspace();

        ws.Execute(new ApplyScene([new SceneEntry(1, "IDE", Workspace.WorkPanel, ScenePlacement.Center)], []) { View = FocusPosition.Right });

        Assert.Equal(FocusPosition.Right, ws.View);
        ws.Execute(new ApplyScene([], [])); // sin vista: se queda la actual
        Assert.Equal(FocusPosition.Right, ws.View);
    }
}
