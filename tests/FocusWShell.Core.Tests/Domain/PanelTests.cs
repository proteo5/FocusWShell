using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

/// <summary>Paneles como entidades (ADR 0011).</summary>
public class PanelTests
{
    private const string Aux = "HP";
    private const nint Slack = 1;
    private const nint Edge = 2;
    private const nint Grafana = 3;

    [Fact]
    public void StartsWithTwoPanelsAroundTheFocus()
    {
        var ws = new Workspace();

        Assert.Equal([Workspace.ContextPanel, Workspace.WorkPanel], ws.Panels.Select(p => p.Id));
        Assert.Equal(PanelSide.Left, ws.FindPanel(Workspace.ContextPanel)!.Side);
        Assert.All(ws.Panels, p => Assert.Null(p.Display));
    }

    [Fact]
    public void RenameChangesOnlyTheName()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(Slack, "Slack", Workspace.ContextPanel));

        var outcome = ws.Execute(new RenamePanel(Workspace.ContextPanel, "  Comunicación "));

        Assert.True(outcome.Accepted);
        Assert.Empty(outcome.Effects); // no mueve nada
        Assert.Equal("Comunicación", ws.FindPanel(Workspace.ContextPanel)!.Name);
        Assert.Equal([Slack], ws.Composition.Context);
    }

    [Fact]
    public void CreateAnAuxiliaryPanelAndAssignWindows()
    {
        var ws = new Workspace();

        Assert.True(ws.Execute(new CreatePanel(new Panel("monitoreo", "Monitoreo", Aux, PanelSide.Right, PanelTemplate.Fill))).Accepted);
        ws.Execute(new Adopt(Grafana, "Grafana", "monitoreo"));

        var aux = ws.Composition.Aux.Single();
        Assert.Equal(Aux, aux.Display);
        Assert.Equal([Grafana], aux.Panels.Single().Windows);
        Assert.Equal(PanelTemplate.Fill, aux.Panels.Single().Panel.Template);
    }

    [Fact]
    public void InvalidPanelsAreRejected()
    {
        var ws = new Workspace();

        Assert.False(ws.Execute(new CreatePanel(new Panel(Workspace.WorkPanel, "Otro", Aux, PanelSide.Left))).Accepted); // id repetido
        Assert.False(ws.Execute(new CreatePanel(new Panel("x", " ", Aux, PanelSide.Left))).Accepted); // sin nombre
        Assert.False(ws.Execute(new CreatePanel(new Panel("x", "X", null, PanelSide.Left))).Accepted); // columna ya ocupada
        Assert.False(ws.Execute(new CreatePanel(new Panel("x", "X", Aux, PanelSide.Left, PanelTemplate.Fill) with { Display = null })).Accepted); // Fill en el principal
        Assert.False(ws.Execute(new Adopt(Slack, "Slack", "no-existe")).Accepted);
    }

    [Fact]
    public void MovePanelTakesItsWindowsAlong()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(Slack, "Slack", Workspace.ContextPanel));
        ws.Execute(new CreatePanel(new Panel("chat", "Chat", Aux, PanelSide.Left)));
        ws.Execute(new SendTo(Slack, "chat"));
        Assert.Equal([Slack], ws.Composition.Aux.Single().Context);

        var outcome = ws.Execute(new MovePanel("chat", null, PanelSide.Left));

        Assert.False(outcome.Accepted); // ya hay un panel a la izquierda del foco (etapa 1)
        ws.Execute(new DeletePanel(Workspace.ContextPanel));
        Assert.True(ws.Execute(new MovePanel("chat", null, PanelSide.Left)).Accepted);
        Assert.Equal([Slack], ws.Composition.Context);
        Assert.Empty(ws.Composition.Aux);
    }

    [Fact]
    public void DeletingAPanelLeavesItsWindowsFreeWhereTheyAre()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(Slack, "Slack", Workspace.ContextPanel));
        ws.Execute(new Adopt(Edge, "Edge", Workspace.WorkPanel));

        var outcome = ws.Execute(new DeletePanel(Workspace.ContextPanel));

        Assert.True(outcome.Accepted);
        Assert.Contains(new ForgetOriginal(Slack), outcome.Effects);
        Assert.DoesNotContain(outcome.Effects, e => e is RestoreOriginal);
        Assert.False(ws.IsManaged(Slack));
        Assert.True(ws.IsManaged(Edge));
        Assert.Null(ws.Composition.Left);
    }

    [Fact]
    public void ThePrincipalNeverRunsOutOfPanels()
    {
        var ws = new Workspace();
        ws.Execute(new DeletePanel(Workspace.ContextPanel));

        Assert.False(ws.Execute(new DeletePanel(Workspace.WorkPanel)).Accepted);
    }

    [Fact]
    public void FillOnlyOnAuxiliaryPanels()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(new Panel("mon", "Monitoreo", Aux, PanelSide.Right)));

        Assert.False(ws.Execute(new SetPresentation(Workspace.WorkPanel, PanelTemplate.Fill)).Accepted);
        Assert.True(ws.Execute(new SetPresentation("mon", PanelTemplate.Fill)).Accepted);
        Assert.False(ws.Execute(new MovePanel("mon", null, PanelSide.Right)).Accepted); // un Fill no puede ir al principal
    }

    [Fact]
    public void RestoreAllKeepsThePanels()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(new Panel("mon", "Monitoreo", Aux, PanelSide.Right)));
        ws.Execute(new Adopt(Grafana, "Grafana", "mon"));

        ws.Execute(new RestoreAll());

        Assert.Empty(ws.Windows);
        Assert.Equal(3, ws.Panels.Count);
    }
}
