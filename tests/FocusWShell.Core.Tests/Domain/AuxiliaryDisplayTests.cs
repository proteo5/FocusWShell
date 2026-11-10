using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

/// <summary>Ventanas en monitores auxiliares (ADR 0008).</summary>
public class AuxiliaryDisplayTests
{
    private const string Aux = @"\\.\DISPLAY2";
    private const string AuxContext = "aux-contexto";
    private const string AuxWork = "aux-trabajo";
    private const nint Slack = 1;
    private const nint Edge = 2;
    private const nint Grafana = 3;
    private const nint Logs = 4;

    /// <summary>Principal: Slack en Contexto, Edge en Trabajo · Auxiliar: Grafana y Logs en Trabajo.</summary>
    private static Workspace Sample()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(new Panel(AuxContext, "Contexto aux", Aux, PanelSide.Left)));
        ws.Execute(new CreatePanel(new Panel(AuxWork, "Trabajo aux", Aux, PanelSide.Right)));
        ws.Execute(new Adopt(Slack, "Slack", Workspace.ContextPanel));
        ws.Execute(new Adopt(Edge, "Edge", Workspace.WorkPanel));
        ws.Execute(new Adopt(Grafana, "Grafana", AuxWork));
        ws.Execute(new Adopt(Logs, "Logs", AuxWork));
        return ws;
    }

    [Fact]
    public void AuxWindowsAreNotInThePrincipalPanels()
    {
        var c = Sample().Composition;

        Assert.Equal([Edge], c.Work);
        var aux = Assert.Single(c.Aux);
        Assert.Equal(Aux, aux.Display);
        Assert.Equal([Grafana, Logs], aux.Work);
        Assert.Empty(aux.Context);
        Assert.Equal(4, c.All.Count());
    }

    [Fact]
    public void PromoteFromAuxGoesToThePrincipalCenterAndComesBack()
    {
        var ws = Sample();

        ws.Execute(new Promote(Grafana));
        Assert.Equal(Grafana, ws.Composition.Center);
        Assert.Equal([Logs], ws.Composition.Aux.Single().Work);

        ws.Execute(new ReturnToPanel(Grafana));
        Assert.Equal([Grafana, Logs], ws.Composition.Aux.Single().Work);
    }

    [Fact]
    public void SendToAnAuxiliaryPanel()
    {
        var ws = Sample();

        ws.Execute(new SendTo(Slack, AuxContext));

        Assert.Empty(ws.Composition.Context);
        Assert.Equal([Slack], ws.Composition.Aux.Single().Context);
        Assert.Equal(AuxContext, ws.Find(Slack)!.PanelId);
    }

    [Fact]
    public void SendToOnCenterChangesWhereItReturns()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        var outcome = ws.Execute(new SendTo(Edge, AuxWork));
        Assert.DoesNotContain(outcome.Effects, e => e is ApplyLayout);

        ws.Execute(new ReturnToPanel(Edge));
        Assert.Equal([Grafana, Logs, Edge], ws.Composition.Aux.Single().Work);
    }

    [Fact]
    public void FillIsOnlyForAuxiliaryDisplays()
    {
        var ws = Sample();

        Assert.False(ws.Execute(new SetPresentation(Workspace.WorkPanel, PanelTemplate.Fill)).Accepted);
        Assert.True(ws.Execute(new SetPresentation(AuxWork, PanelTemplate.Fill)).Accepted);
        Assert.Equal(PanelTemplate.Fill, ws.Composition.Aux.Single().WorkTemplate);
        Assert.Equal(PanelTemplate.Tiles, ws.Composition.WorkTemplate);
    }

    [Fact]
    public void PrincipalPanelAllowsStack()
    {
        var ws = Sample();

        var outcome = ws.Execute(new SetPresentation(Workspace.WorkPanel, PanelTemplate.Stack));

        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Equal(PanelTemplate.Stack, ws.Composition.WorkTemplate);
    }

    [Fact]
    public void RemovedDisplaySendsItsWindowsToThePrincipalPanelsNeverTheCenter()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        var outcome = ws.Execute(new DisplayRemoved(Aux));

        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Empty(ws.Composition.Aux);
        Assert.Equal(Edge, ws.Composition.Center);
        Assert.Equal([Grafana, Logs], ws.Composition.Work);
    }

    [Fact]
    public void ReconnectedDisplayGetsItsWindowsAndTemplatesBack()
    {
        // S6: desconectar y reconectar no pierde nada.
        var ws = Sample();
        ws.Execute(new SetPresentation(AuxWork, PanelTemplate.Fill));

        ws.Execute(new DisplayRemoved(Aux));
        Assert.Equal([Edge, Grafana, Logs], ws.Composition.Work);
        Assert.Equal(Aux, ws.FindPanel(ws.Find(Grafana)!.PanelId)!.Display);
        Assert.True(ws.IsDisplayMissing(Aux));

        var outcome = ws.Execute(new DisplayAdded(Aux));

        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Equal([Edge], ws.Composition.Work);
        var aux = ws.Composition.Aux.Single();
        Assert.Equal([Grafana, Logs], aux.Work);
        Assert.Equal(PanelTemplate.Fill, aux.WorkTemplate);
    }

    [Fact]
    public void RemovingTwiceOrAddingAKnownDisplayDoesNothing()
    {
        var ws = Sample();
        ws.Execute(new DisplayRemoved(Aux));

        Assert.Empty(ws.Execute(new DisplayRemoved(Aux)).Effects);
        ws.Execute(new DisplayAdded(Aux));
        Assert.Empty(ws.Execute(new DisplayAdded(Aux)).Effects);
    }

    [Fact]
    public void WindowSentToAMissingDisplayWaitsOnThePrincipal()
    {
        var ws = Sample();
        ws.Execute(new DisplayRemoved(Aux));

        ws.Execute(new SendTo(Slack, AuxContext));
        Assert.Equal([Slack], ws.Composition.Context);

        ws.Execute(new DisplayAdded(Aux));
        Assert.Empty(ws.Composition.Context);
        Assert.Equal([Slack], ws.Composition.Aux.Single().Context);
    }

    [Fact]
    public void MaximizingAWindowWhoseDisplayIsMissingDetachesItInsteadOfFill()
    {
        var ws = Sample();
        ws.Execute(new DisplayRemoved(Aux));

        Assert.Equal([new Detach(Grafana)], ws.Interpret(Grafana, ExternalChange.Maximized));
    }

    [Fact]
    public void RemovingAnUnknownDisplayDoesNothing()
    {
        var outcome = Sample().Execute(new DisplayRemoved(@"\\.\DISPLAY9"));

        Assert.True(outcome.Accepted);
        Assert.Empty(outcome.Effects);
    }
}
