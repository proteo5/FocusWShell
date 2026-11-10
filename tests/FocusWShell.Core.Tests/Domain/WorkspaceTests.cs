using FocusWShell.Core.Domain;

namespace FocusWShell.Core.Tests.Domain;

public class WorkspaceTests
{
    private const nint Slack = 1;
    private const nint Edge = 2;
    private const nint Explorer = 3;
    private const nint GitHub = 4;

    /// <summary>Contexto: Slack · Trabajo: Explorer, GitHub, Edge.</summary>
    private static Workspace Sample()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(Slack, "Slack", Home.Context));
        ws.Execute(new Adopt(Explorer, "Explorer", Home.Work));
        ws.Execute(new Adopt(GitHub, "GitHub", Home.Work));
        ws.Execute(new Adopt(Edge, "Edge", Home.Work));
        return ws;
    }

    /// <summary>Invariante 1: como máximo una ventana en el Centro.</summary>
    private static void AssertInvariants(Workspace ws)
    {
        Assert.True(ws.Windows.Count(w => w.Placement == Placement.Center) <= 1);
        Assert.Equal(ws.Windows.Count, ws.Windows.Select(w => w.Handle).Distinct().Count());
        Assert.All(ws.CenterHistory, h => Assert.True(ws.IsManaged(h)));
    }

    [Fact]
    public void AdoptCapturesOriginalBeforeLayout()
    {
        var ws = new Workspace();

        var outcome = ws.Execute(new Adopt(Slack, "Slack", Home.Context));

        Assert.True(outcome.Accepted);
        Assert.Collection(outcome.Effects,
            e => Assert.Equal(new CaptureOriginal(Slack), e),
            e => Assert.IsType<ApplyLayout>(e));
        Assert.Equal(new ManagedWindow(Slack, "Slack", Home.Context, Placement.InPanel), ws.Find(Slack));
    }

    [Fact]
    public void AdoptTwiceIsRejected()
    {
        var ws = Sample();

        var outcome = ws.Execute(new Adopt(Slack, "Slack", Home.Work));

        Assert.False(outcome.Accepted);
        Assert.Equal(Home.Context, ws.Find(Slack)!.Home);
    }

    [Fact]
    public void CompositionKeepsPanelOrder()
    {
        var c = Sample().Composition;

        Assert.Equal([Slack], c.Context);
        Assert.Null(c.Center);
        Assert.Equal([Explorer, GitHub, Edge], c.Work);
    }

    [Fact]
    public void PromoteMovesWindowToCenterAndActivatesIt()
    {
        var ws = Sample();

        var outcome = ws.Execute(new Promote(Edge));

        Assert.True(outcome.Accepted);
        Assert.Contains(new Activate(Edge), outcome.Effects);
        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Equal(Edge, ws.Composition.Center);
        Assert.Equal([Explorer, GitHub], ws.Composition.Work);
        Assert.Equal(Home.Work, ws.Find(Edge)!.Home);
        AssertInvariants(ws);
    }

    [Fact]
    public void PromoteSendsPreviousCenterBackToItsSlot()
    {
        var ws = Sample();
        ws.Execute(new Promote(Explorer));

        ws.Execute(new Promote(Edge));

        Assert.Equal(Edge, ws.Composition.Center);
        Assert.Equal([Explorer, GitHub], ws.Composition.Work); // Explorer vuelve a ser el primero
        Assert.Equal([Explorer], ws.CenterHistory);
        AssertInvariants(ws);
    }

    [Fact]
    public void PromoteFromContextReturnsToContext()
    {
        var ws = Sample();
        ws.Execute(new Promote(Slack));
        Assert.Empty(ws.Composition.Context);

        ws.Execute(new Promote(Edge));

        Assert.Equal([Slack], ws.Composition.Context);
        Assert.Equal(Home.Context, ws.Find(Slack)!.Home);
    }

    [Fact]
    public void PromoteRejectsUnmanagedAndCurrentCenter()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        Assert.False(ws.Execute(new Promote(99)).Accepted);
        Assert.False(ws.Execute(new Promote(Edge)).Accepted);
    }

    [Fact]
    public void SwapWithPreviousGoesBackInOneAction()
    {
        // CA2: una app de Trabajo pasa al Centro y la anterior se recupera en menos de dos acciones.
        var ws = Sample();
        ws.Execute(new Promote(Edge));
        ws.Execute(new Promote(GitHub));

        var outcome = ws.Execute(new SwapWithPrevious());

        Assert.True(outcome.Accepted);
        Assert.Contains(new Activate(Edge), outcome.Effects);
        Assert.Equal(Edge, ws.Composition.Center);
        Assert.Equal([GitHub], ws.CenterHistory);
        AssertInvariants(ws);
    }

    [Fact]
    public void SwapTwiceTogglesBetweenTheTwo()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));
        ws.Execute(new Promote(GitHub));

        ws.Execute(new SwapWithPrevious());
        ws.Execute(new SwapWithPrevious());

        Assert.Equal(GitHub, ws.Composition.Center);
    }

    [Fact]
    public void SwapWithoutHistoryIsRejected()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        Assert.False(ws.Execute(new SwapWithPrevious()).Accepted);
    }

    [Fact]
    public void SwapAfterReturnToPanelBringsItBack()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        ws.Execute(new ReturnToPanel(Edge));
        Assert.Null(ws.Composition.Center);
        Assert.Equal([Explorer, GitHub, Edge], ws.Composition.Work);

        ws.Execute(new SwapWithPrevious());
        Assert.Equal(Edge, ws.Composition.Center);
    }

    [Fact]
    public void ReturnToPanelOnlyAcceptsTheCenter()
    {
        Assert.False(Sample().Execute(new ReturnToPanel(Edge)).Accepted);
    }

    [Fact]
    public void SendToMovesBetweenPanelsWithoutTouchingOthers()
    {
        var ws = Sample();
        var before = ws.Windows.Where(w => w.Handle != Explorer).ToList();

        var outcome = ws.Execute(new SendTo(Explorer, Home.Context));

        Assert.True(outcome.Accepted);
        Assert.Equal([Slack, Explorer], ws.Composition.Context);
        Assert.Equal([GitHub, Edge], ws.Composition.Work);
        // Invariante 3: sólo cambia el hogar de la ventana nombrada.
        Assert.All(before, w => Assert.Equal(w.Home, ws.Find(w.Handle)!.Home));
    }

    [Fact]
    public void SendToOnCenterChangesWhereItReturns()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        var outcome = ws.Execute(new SendTo(Edge, Home.Context));
        Assert.DoesNotContain(outcome.Effects, e => e is ApplyLayout); // sigue en el Centro

        ws.Execute(new ReturnToPanel(Edge));
        Assert.Equal([Slack, Edge], ws.Composition.Context);
    }

    [Fact]
    public void ReleaseRestoresAndForgets()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));
        ws.Execute(new Promote(GitHub));

        var outcome = ws.Execute(new Release(Edge));

        Assert.Equal(new RestoreOriginal(Edge), outcome.Effects[0]);
        Assert.False(ws.IsManaged(Edge));
        Assert.DoesNotContain(Edge, ws.CenterHistory);
        Assert.False(ws.Execute(new SwapWithPrevious()).Accepted);
        AssertInvariants(ws);
    }

    [Fact]
    public void ReleaseOfCenterLeavesCenterEmpty()
    {
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        ws.Execute(new Release(Edge));

        Assert.Null(ws.Composition.Center);
    }

    [Fact]
    public void RestoreAllEmptiesTheModel()
    {
        // Invariante 6.
        var ws = Sample();
        ws.Execute(new Promote(Edge));

        var outcome = ws.Execute(new RestoreAll());

        Assert.Equal([new RestoreEverything()], outcome.Effects);
        Assert.Empty(ws.Windows);
        Assert.Empty(ws.CenterHistory);
    }

    [Fact]
    public void ClosingTheCenterDoesNotPromoteAnother()
    {
        var ws = Sample();
        ws.Execute(new Promote(Explorer));
        ws.Execute(new Promote(Edge));

        ws.Execute(new WindowClosed(Edge));

        Assert.Null(ws.Composition.Center);
        Assert.Equal([Explorer], ws.CenterHistory);
        AssertInvariants(ws);
    }

    [Fact]
    public void ClosingUnknownWindowDoesNothing()
    {
        var outcome = Sample().Execute(new WindowClosed(99));

        Assert.True(outcome.Accepted);
        Assert.Empty(outcome.Effects);
    }
}
