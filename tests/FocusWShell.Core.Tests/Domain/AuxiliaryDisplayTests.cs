using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

/// <summary>Ventanas en monitores auxiliares (ADR 0008).</summary>
public class AuxiliaryDisplayTests
{
    private const string Aux = @"\\.\DISPLAY2";
    private const nint Slack = 1;
    private const nint Edge = 2;
    private const nint Grafana = 3;
    private const nint Logs = 4;

    /// <summary>Principal: Slack en Contexto, Edge en Trabajo · Auxiliar: Grafana y Logs en Trabajo.</summary>
    private static Workspace Sample()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(Slack, "Slack", Home.Context));
        ws.Execute(new Adopt(Edge, "Edge", Home.Work));
        ws.Execute(new Adopt(Grafana, "Grafana", Home.Work, Aux));
        ws.Execute(new Adopt(Logs, "Logs", Home.Work, Aux));
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
    public void SendToDisplayMovesThePanelNotTheHome()
    {
        var ws = Sample();

        ws.Execute(new SendToDisplay(Slack, Aux));

        Assert.Empty(ws.Composition.Context);
        Assert.Equal([Slack], ws.Composition.Aux.Single().Context);
        Assert.Equal(Home.Context, ws.Find(Slack)!.Home);
    }

    [Fact]
    public void SendToDisplayOnCenterChangesWhereItReturns()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        var outcome = ws.Execute(new SendToDisplay(Edge, Aux));
        Assert.DoesNotContain(outcome.Effects, e => e is ApplyLayout);

        ws.Execute(new ReturnToPanel(Edge));
        Assert.Equal([Grafana, Logs, Edge], ws.Composition.Aux.Single().Work);
    }

    [Fact]
    public void FillIsOnlyForAuxiliaryDisplays()
    {
        var ws = Sample();

        Assert.False(ws.Execute(new SetTemplate(null, Home.Work, PanelTemplate.Fill)).Accepted);
        Assert.True(ws.Execute(new SetTemplate(Aux, Home.Work, PanelTemplate.Fill)).Accepted);
        Assert.Equal(PanelTemplate.Fill, ws.Composition.Aux.Single().WorkTemplate);
        Assert.Equal(PanelTemplate.Tiles, ws.Composition.WorkTemplate);
    }

    [Fact]
    public void SetTemplateOnPrincipalAllowsStack()
    {
        var ws = Sample();

        var outcome = ws.Execute(new SetTemplate(null, Home.Work, PanelTemplate.Stack));

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
    public void RemovingAnUnknownDisplayDoesNothing()
    {
        var outcome = Sample().Execute(new DisplayRemoved(@"\\.\DISPLAY9"));

        Assert.True(outcome.Accepted);
        Assert.Empty(outcome.Effects);
    }
}
