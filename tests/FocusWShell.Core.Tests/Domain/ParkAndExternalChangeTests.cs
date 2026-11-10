using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

/// <summary>Estacionar, fuera de layout y cómo se interpretan los cambios que hace el usuario.</summary>
public class ParkAndExternalChangeTests
{
    private const string Aux = @"\\.\DISPLAY2";
    private const string AuxContext = "aux-contexto";
    private const string AuxWork = "aux-trabajo";
    private const nint Slack = 1;
    private const nint Edge = 2;
    private const nint Explorer = 3;
    private const nint GitHub = 4;
    private const nint Grafana = 5;
    private const nint Logs = 6;

    /// <summary>Contexto: Slack · Trabajo: Explorer, GitHub, Edge · Auxiliar Trabajo: Grafana, Logs.</summary>
    private static Workspace Sample()
    {
        var ws = new Workspace();
        ws.Execute(new CreatePanel(new Panel(AuxContext, "Contexto aux", Aux, PanelSide.Left)));
        ws.Execute(new CreatePanel(new Panel(AuxWork, "Trabajo aux", Aux, PanelSide.Right)));
        ws.Execute(new Adopt(Slack, "Slack", Workspace.ContextPanel));
        ws.Execute(new Adopt(Explorer, "Explorer", Workspace.WorkPanel));
        ws.Execute(new Adopt(GitHub, "GitHub", Workspace.WorkPanel));
        ws.Execute(new Adopt(Edge, "Edge", Workspace.WorkPanel));
        ws.Execute(new Adopt(Grafana, "Grafana", AuxWork));
        ws.Execute(new Adopt(Logs, "Logs", AuxWork));
        return ws;
    }

    private static void Run(Workspace ws, IEnumerable<Command> commands)
    {
        foreach (var c in commands)
        {
            Assert.True(ws.Execute(c).Accepted, $"rechazado: {c}");
        }
    }

    [Fact]
    public void ParkRemovesFromPanelAndKeepsHome()
    {
        var ws = Sample();

        var outcome = ws.Execute(new Park(GitHub));

        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Equal([Explorer, Edge], ws.Composition.Work);
        Assert.Equal([GitHub], ws.Composition.Parked);
        Assert.Equal(Workspace.WorkPanel, ws.Find(GitHub)!.PanelId);
    }

    [Fact]
    public void RestoreReturnsToTheSameSlot()
    {
        // CA4: una app estacionada se restaura a su panel original.
        var ws = Sample();
        ws.Execute(new Park(GitHub));

        ws.Execute(new Restore(GitHub));

        Assert.Equal([Explorer, GitHub, Edge], ws.Composition.Work);
        Assert.Empty(ws.Composition.Parked);
    }

    [Fact]
    public void ParkingTheCenterEmptiesItAndSwapBringsItBack()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        ws.Execute(new Park(Edge));
        Assert.Null(ws.Composition.Center);

        ws.Execute(new SwapWithPrevious());
        Assert.Equal(Edge, ws.Composition.Center);
        Assert.Empty(ws.Composition.Parked);
    }

    [Fact]
    public void RestoreOnlyAcceptsParked()
    {
        Assert.False(Sample().Execute(new Restore(Edge)).Accepted);
    }

    [Fact]
    public void DetachedWindowLeavesThePanelUntilReattached()
    {
        var ws = Sample();

        ws.Execute(new Detach(GitHub));
        Assert.Equal([Explorer, Edge], ws.Composition.Work);
        Assert.True(ws.Find(GitHub)!.Detached);
        Assert.DoesNotContain(GitHub, ws.Composition.All);

        ws.Execute(new Reattach(GitHub));
        Assert.Equal([Explorer, GitHub, Edge], ws.Composition.Work);
    }

    [Fact]
    public void DetachedCenterLeavesTheCenterColumnEmpty()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        ws.Execute(new Detach(Edge));

        Assert.Null(ws.Composition.Center);
        Assert.Equal(Edge, ws.Center!.Handle);
    }

    [Fact]
    public void PromoteReattaches()
    {
        var ws = Sample();
        ws.Execute(new Detach(GitHub));

        ws.Execute(new Promote(GitHub));

        Assert.Equal(GitHub, ws.Composition.Center);
        Assert.False(ws.Find(GitHub)!.Detached);
    }

    [Fact]
    public void MakeActiveInPanelPutsItFirst()
    {
        var ws = Sample();

        ws.Execute(new MakeActiveInPanel(Edge));

        Assert.Equal([Edge, Explorer, GitHub], ws.Composition.Work);
    }

    [Fact]
    public void HiddenByLayoutFollowsTheTemplate()
    {
        var ws = Sample();
        Assert.False(ws.IsHiddenByLayout(GitHub));

        ws.Execute(new SetPresentation(Workspace.WorkPanel, PanelTemplate.Stack));

        Assert.False(ws.IsHiddenByLayout(Explorer));
        Assert.True(ws.IsHiddenByLayout(GitHub));
    }

    // --- Interpretación de cambios externos ---------------------------------------------------

    [Fact]
    public void UserMinimizeParks()
    {
        var ws = Sample();

        Run(ws, ws.Interpret(GitHub, ExternalChange.Minimized));

        Assert.Equal([GitHub], ws.Composition.Parked);
    }

    [Fact]
    public void UserShowsParkedWindowFromTaskbarRestoresIt()
    {
        var ws = Sample();
        ws.Execute(new Park(GitHub));

        Run(ws, ws.Interpret(GitHub, ExternalChange.Shown));

        Assert.Equal([Explorer, GitHub, Edge], ws.Composition.Work);
    }

    [Fact]
    public void UserShowsHiddenStackWindowMakesItTheVisibleTab()
    {
        var ws = Sample();
        ws.Execute(new SetPresentation(Workspace.WorkPanel, PanelTemplate.Stack));

        Run(ws, ws.Interpret(Edge, ExternalChange.Shown));

        Assert.Equal(Edge, ws.Composition.Work[0]);
        Assert.Empty(ws.Composition.Parked);
    }

    [Fact]
    public void MaximizeOnAuxiliaryMeansFill()
    {
        var ws = Sample();

        Run(ws, ws.Interpret(Logs, ExternalChange.Maximized));

        var aux = ws.Composition.Aux.Single();
        Assert.Equal(PanelTemplate.Fill, aux.WorkTemplate);
        Assert.Equal(Logs, aux.Work[0]);
    }

    [Fact]
    public void UnmaximizeFillWindowReturnsPanelToTiles()
    {
        var ws = Sample();
        ws.Execute(new SetPresentation(AuxWork, PanelTemplate.Fill));

        Run(ws, ws.Interpret(Grafana, ExternalChange.Unmaximized));

        Assert.Equal(PanelTemplate.Tiles, ws.Composition.Aux.Single().WorkTemplate);
    }

    [Fact]
    public void MaximizeOnPrincipalDetaches()
    {
        var ws = Sample();

        Run(ws, ws.Interpret(Explorer, ExternalChange.Maximized));

        Assert.True(ws.Find(Explorer)!.Detached);
        Assert.Equal([GitHub, Edge], ws.Composition.Work);
    }

    [Fact]
    public void DragDetachesAndRepeatedDragsDoNothingMore()
    {
        var ws = Sample();

        Run(ws, ws.Interpret(Slack, ExternalChange.MovedOrResized));
        var again = ws.Interpret(Slack, ExternalChange.MovedOrResized).Select(ws.Execute).ToList();

        Assert.True(ws.Find(Slack)!.Detached);
        Assert.All(again, o => Assert.Empty(o.Effects));
    }

    [Fact]
    public void ClosedWindowIsForgotten()
    {
        var ws = Sample();

        Run(ws, ws.Interpret(Edge, ExternalChange.Closed));

        Assert.False(ws.IsManaged(Edge));
    }

    [Fact]
    public void NoExternalChangeEverPromotes()
    {
        // CA7: nada de lo que haga el usuario fuera del gestor lleva una ventana al Centro.
        foreach (var change in Enum.GetValues<ExternalChange>())
        {
            var ws = Sample();
            ws.Execute(new Park(GitHub));
            Assert.DoesNotContain(ws.Interpret(GitHub, change), c => c is Promote or SwapWithPrevious);
            Assert.DoesNotContain(ws.Interpret(Edge, change), c => c is Promote or SwapWithPrevious);
        }
    }

    [Fact]
    public void UnknownWindowsAreIgnored()
    {
        Assert.Empty(Sample().Interpret(99, ExternalChange.Minimized));
    }
}
