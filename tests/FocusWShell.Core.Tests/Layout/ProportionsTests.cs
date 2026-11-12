using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Proporciones por vista: presets y separador arrastrado (ADR 0012).</summary>
public class ProportionsTests
{
    // Ultrawide, foco al centro: izquierda 0–619, foco 619–2511, derecha 2511–3440.
    private static readonly PixelRect Work = new(0, 0, 3440, 1392);

    private static SceneLayoutResult Layout(FocusPosition focus = FocusPosition.Center)
    {
        var w = ViewProportions.Default(focus);
        return SceneLayoutEngine.Compute(new SceneInput(
            Work,
            new PanelInput("contexto", PanelSide.Left, w.Left, PanelTemplate.Tiles, []),
            w.Center,
            null,
            new PanelInput("trabajo", PanelSide.Right, w.Right, PanelTemplate.Tiles, []))
        {
            Focus = focus,
        });
    }

    [Fact]
    public void DraggingTheFocusRightEdgeMovesTheSeparatorWithWork()
    {
        var weights = ColumnSeparator.Detect(new(619, 0, 2511, 1392), new(619, 0, 2300, 1392), Layout());

        Assert.Equal(new ViewWeights(619, 1681, 1140), weights);
    }

    [Fact]
    public void DraggingAPanelsInnerEdgeMovesTheSeparatorWithTheFocus()
    {
        var weights = ColumnSeparator.Detect(new(0, 0, 619, 1392), new(0, 0, 800, 1392), Layout());

        Assert.Equal(new ViewWeights(800, 1711, 929), weights);
    }

    [Fact]
    public void InTheFocusOnTheLeftViewTheSeparatorsFollowTheScreenOrder()
    {
        // Foco 0–1892 · derecha (Trabajo) 1892–2924 · izquierda (Contexto) 2924–3440.
        var weights = ColumnSeparator.Detect(new(1892, 0, 2924, 1392), new(1892, 0, 3100, 1392), Layout(FocusPosition.Left));

        Assert.Equal(new ViewWeights(340, 1892, 1208), weights);
    }

    [Theory]
    [InlineData(-50, 0, 619, 1392)] // el borde del monitor no es un separador
    [InlineData(100, 0, 719, 1392)] // movida entera
    [InlineData(0, 40, 800, 1392)] // también cambió el borde de arriba
    [InlineData(0, 0, 619, 1392)] // no cambió nada en horizontal
    public void AnythingElseIsNotASeparator(int left, int top, int right, int bottom)
    {
        Assert.Null(ColumnSeparator.Detect(new(0, 0, 619, 1392), new(left, top, right, bottom), Layout()));
    }

    [Fact]
    public void AWindowNotAlignedWithItsColumnIsNotASeparator()
    {
        // Ventana del foco más ancha que su columna, centrada sobre los paneles: sus bordes no son los de la columna.
        Assert.Null(ColumnSeparator.Detect(new(500, 0, 2630, 1392), new(500, 0, 2400, 1392), Layout()));
    }

    [Fact]
    public void ADragNeverLeavesTheNeighbourWithNothing()
    {
        var weights = ColumnSeparator.Detect(new(619, 0, 2511, 1392), new(619, 0, 3430, 1392), Layout());

        Assert.Equal(ColumnSeparator.MinColumnWidth, weights!.Right);
    }

    [Fact]
    public void ProportionsAreRememberedPerView()
    {
        var ws = new Workspace();
        var custom = new ViewWeights(619, 1681, 1140);

        var current = ws.Execute(new SetProportions(FocusPosition.Center, custom));
        var other = ws.Execute(new SetProportions(FocusPosition.Left, new ViewWeights(10, 70, 20)));

        Assert.Equal([new ApplyLayout()], current.Effects);
        Assert.Empty(other.Effects); // no es la vista actual: no se mueve nada
        Assert.Equal(custom, ws.Composition.Weights);
        ws.Execute(new SetView(FocusPosition.Left));
        Assert.Equal(new ViewWeights(10, 70, 20), ws.Composition.Weights);
        Assert.Equal(ViewProportions.Default(FocusPosition.Right), ws.ProportionsOf(FocusPosition.Right));
    }

    [Fact]
    public void BackToTheDefaultsForgetsTheCustomOnes()
    {
        var ws = new Workspace();
        ws.Execute(new SetProportions(FocusPosition.Center, new ViewWeights(1, 1, 1)));

        ws.Execute(new SetProportions(FocusPosition.Center, null));

        Assert.Empty(ws.CustomProportions);
        Assert.False(ws.Execute(new SetProportions(FocusPosition.Center, new ViewWeights(0, 1, 1))).Accepted);
    }

    [Fact]
    public void ASceneBringsItsProportions()
    {
        var ws = new Workspace();

        ws.Execute(new ApplyScene([], []) { View = FocusPosition.Right, Weights = new ViewWeights(10, 45, 45) });

        Assert.Equal(new ViewWeights(10, 45, 45), ws.ProportionsOf(FocusPosition.Right));
        Assert.True(new ViewWeights(10, 45, 45).SameAs(ws.Composition.Weights));
    }
}
