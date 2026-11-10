using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Presentación Lista (ADR 0015): las ventanas van detrás de la lista, sin minimizar.</summary>
public class ListPanelTests
{
    private static readonly PixelRect Aux = new(3440, 0, 5360, 1020);

    private static LayoutWindow Win(int id, int minW = 0, int minH = 0) =>
        new(id, $"App{id}", minW == 0 && minH == 0 ? null : new PixelSize(minW, minH));

    [Fact]
    public void WindowsGoBehindTheListInItsArea()
    {
        var r = AuxLayoutEngine.Compute(new AuxInput("aux", Aux,
            [new PanelInput("monitoreo", PanelSide.Left, 1, PanelTemplate.List, [Win(1), Win(2)])], TopInset: 35));

        var area = new PixelRect(3440, 35, 5360, 1020);
        Assert.Equal(area, r.Lists["monitoreo"]);
        Assert.All(r.Targets, t => Assert.Equal((TargetKind.Visible, area), (t.Kind, t.Rect)));
        Assert.Equal(PanelTemplate.List, r.EffectiveTemplates["monitoreo"]);
    }

    [Fact]
    public void AWindowThatDoesNotFitIsMinimizedInsteadOfShowingOutsideTheList()
    {
        var r = SceneLayoutEngine.Compute(new SceneInput(
            new PixelRect(0, 0, 3440, 1392),
            new PanelInput("contexto", PanelSide.Left, 18, PanelTemplate.List, [Win(1), Win(2, minW: 900)]),
            55,
            null,
            new PanelInput("trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, [])));

        // La lista no ensancha la columna: la ventana ancha se minimiza.
        Assert.Equal(619, r.LeftColumn.Width);
        Assert.Equal(TargetKind.Minimized, r.Targets.Single(t => t.Handle == 2).Kind);
        Assert.Equal(TargetKind.Visible, r.Targets.Single(t => t.Handle == 1).Kind);
        Assert.Contains(r.Notes, n => n.Contains("detrás de la lista", StringComparison.Ordinal));
    }

    [Fact]
    public void TheListModeIsPerPanelAndMovesNothing()
    {
        var ws = new Workspace();
        ws.Execute(new SetPresentation(Workspace.WorkPanel, PanelTemplate.List));

        var outcome = ws.Execute(new SetListLayout(Workspace.WorkPanel, ListLayout.Details));

        Assert.True(outcome.Accepted);
        Assert.Empty(outcome.Effects);
        Assert.Equal(ListLayout.Details, ws.FindPanel(Workspace.WorkPanel)!.ListLayout);
        Assert.Equal(ListLayout.LargeIcons, ws.FindPanel(Workspace.ContextPanel)!.ListLayout);
    }

    [Fact]
    public void AnEmptyListPanelStillGetsItsAreaAsALauncher()
    {
        var aux = AuxLayoutEngine.Compute(new AuxInput("aux", Aux,
            [new PanelInput("lanzador", PanelSide.Left, 1, PanelTemplate.List, []), new PanelInput("trabajo", PanelSide.Right, 1, PanelTemplate.Tiles, [Win(1)])],
            TopInset: 35));
        var principal = SceneLayoutEngine.Compute(new SceneInput(
            new PixelRect(0, 0, 3440, 1392),
            new PanelInput("contexto", PanelSide.Left, 18, PanelTemplate.List, []),
            55,
            null,
            new PanelInput("trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, [])));

        Assert.Equal(new PixelRect(3440, 35, 4400, 1020), aux.Lists["lanzador"]);
        Assert.Equal(new PixelRect(0, 0, 619, 1392), principal.Lists["contexto"]);
    }

    [Fact]
    public void TheCompositionKeepsAnEmptyListPanelOnItsMonitor()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(new Panel("lanzador", "Lanzador", "HP", PanelSide.Left, PanelTemplate.List)));
        ws.Execute(new CreatePanel(new Panel("tiles", "Tiles", "HP", PanelSide.Right)));

        var aux = ws.Composition.Aux.Single();

        Assert.Equal(["lanzador"], aux.Panels.Select(p => p.Panel.Id)); // el de tiles vacío no ocupa sitio
    }
}
