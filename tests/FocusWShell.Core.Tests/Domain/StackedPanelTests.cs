using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

/// <summary>Como máximo dos paneles por columna, uno arriba y otro abajo (ADR 0011).</summary>
public class StackedPanelTests
{
    private const string Aux = "HP";
    private const nint Slack = 1;
    private const nint OneNote = 2;

    private static Panel Notes => new("notas", "Notas", null, PanelSide.Left, PanelTemplate.Tiles, PanelSlot.Lower);

    [Fact]
    public void ASecondPanelGoesBelowTheFirstInTheSameColumn()
    {
        var ws = new Workspace();

        Assert.True(ws.Execute(new CreatePanel(Notes)).Accepted);
        ws.Execute(new Adopt(Slack, "Slack", Workspace.ContextPanel));
        ws.Execute(new Adopt(OneNote, "OneNote", "notas"));

        var c = ws.Composition;
        Assert.Equal([Slack], c.Context);
        Assert.Equal("notas", c.LeftLower!.Panel.Id);
        Assert.Equal([OneNote], c.LeftLower.Windows);
        Assert.Contains(OneNote, c.All);
        Assert.Equal(["contexto", "notas", "trabajo"], c.PrincipalPanels.Select(v => v.Panel.Id));
    }

    [Fact]
    public void AThirdPanelInTheColumnIsRejected()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(Notes));

        var upper = ws.Execute(new CreatePanel(new Panel("x", "X", null, PanelSide.Left)));
        var lower = ws.Execute(new CreatePanel(new Panel("y", "Y", null, PanelSide.Left, PanelTemplate.Tiles, PanelSlot.Lower)));

        Assert.False(upper.Accepted);
        Assert.Contains("Contexto", upper.Rejection, StringComparison.Ordinal);
        Assert.False(lower.Accepted);
        Assert.Contains("abajo", lower.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void AuxiliaryColumnsAlsoHoldTwoAtMost()
    {
        var ws = new Workspace();

        Assert.True(ws.Execute(new CreatePanel(new Panel("a", "A", Aux, PanelSide.Left))).Accepted);
        Assert.True(ws.Execute(new CreatePanel(new Panel("b", "B", Aux, PanelSide.Left, PanelTemplate.Tiles, PanelSlot.Lower))).Accepted);
        Assert.False(ws.Execute(new CreatePanel(new Panel("c", "C", Aux, PanelSide.Left))).Accepted);
        Assert.True(ws.Execute(new CreatePanel(new Panel("d", "D", Aux, PanelSide.Right))).Accepted);
    }

    [Fact]
    public void MovingAPanelToTheFreeSlotOfAnotherColumn()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(Notes));
        ws.Execute(new Adopt(OneNote, "OneNote", "notas"));

        var outcome = ws.Execute(new MovePanel("notas", null, PanelSide.Right, PanelSlot.Lower));

        Assert.True(outcome.Accepted);
        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Null(ws.Composition.LeftLower);
        Assert.Equal([OneNote], ws.Composition.RightLower!.Windows);
        Assert.False(ws.Execute(new MovePanel("notas", null, PanelSide.Right, PanelSlot.Upper)).Accepted); // lo ocupa Trabajo
    }

    [Fact]
    public void ASceneAddsItsLowerPanelOrUsesTheOneInThatSlot()
    {
        var ws = new Workspace();
        var scenePanels = new[]
        {
            new Panel("notas-escena", "Notas", null, PanelSide.Left, PanelTemplate.Stack, PanelSlot.Lower),
            new Panel("otro-arriba", "Otro", null, PanelSide.Right),
        };

        ws.Execute(new ApplyScene(
            [new SceneEntry(Slack, "Slack", "otro-arriba", ScenePlacement.InPanel), new SceneEntry(OneNote, "OneNote", "notas-escena", ScenePlacement.InPanel)],
            scenePanels));

        Assert.Equal(PanelSlot.Lower, ws.FindPanel("notas-escena")!.Slot);
        Assert.Null(ws.FindPanel("otro-arriba")); // arriba a la derecha ya está Trabajo
        Assert.Equal(Workspace.WorkPanel, ws.Find(Slack)!.PanelId);
        Assert.Equal([OneNote], ws.Composition.LeftLower!.Windows);
    }

    [Fact]
    public void ReplacePanelsKeepsBothSlotsButNotTwoInTheSameOne()
    {
        var ws = new Workspace();

        ws.Execute(new ReplacePanels(
        [
            new Panel("a", "A", null, PanelSide.Left),
            Notes,
            new Panel("dup", "Dup", null, PanelSide.Left, PanelTemplate.Tiles, PanelSlot.Lower),
            new Panel("b", "B", null, PanelSide.Right),
        ]));

        Assert.Equal(["a", "notas", "b"], ws.Panels.Select(p => p.Id));
    }

    [Fact]
    public void AScenePanelResolvesToItselfOrToWhoeverHoldsItsSlot()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(Notes));

        Assert.Equal("notas", ws.ResolveScenePanel(new Panel("notas", "Otro nombre", Aux, PanelSide.Right))!.Id); // por identificador
        Assert.Equal(Workspace.WorkPanel, ws.ResolveScenePanel(new Panel("de-otra-escena", "X", null, PanelSide.Right))!.Id); // por puesto
        Assert.Null(ws.ResolveScenePanel(new Panel("nada", "X", Aux, PanelSide.Left))); // nadie en ese puesto
    }
}
