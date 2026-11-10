using FocusWShell.Core.Domain;

namespace FocusWShell.Core.Tests.Domain;

/// <summary>Pantalla completa: el foco maximizado en un monitor (ADR 0012).</summary>
public class FullscreenTests
{
    private const string Aux = "HP";
    private const nint Ide = 1;
    private const nint Edge = 2;

    private static Workspace WithTwo()
    {
        var ws = new Workspace();
        ws.Execute(new Adopt(Ide, "IDE", Workspace.WorkPanel));
        ws.Execute(new Adopt(Edge, "Edge", Workspace.ContextPanel));
        ws.Execute(new Promote(Ide));
        return ws;
    }

    [Fact]
    public void TheFocusGoesFullscreenAndLeavesTheFocusColumnEmpty()
    {
        var ws = WithTwo();

        var outcome = ws.Execute(new EnterFullscreen(Ide, Aux));

        Assert.True(outcome.Accepted);
        Assert.Contains(outcome.Effects, e => e is ApplyLayout);
        Assert.Equal(Placement.Fullscreen, ws.Find(Ide)!.Placement);
        Assert.Null(ws.Composition.Center);
        Assert.Equal(new FullscreenView(Ide, Aux), ws.Composition.Fullscreen);
        Assert.Same(ws.Find(Ide), ws.Focused);
    }

    [Fact]
    public void AWindowFromAPanelIsPromotedFirst()
    {
        var ws = WithTwo();

        ws.Execute(new EnterFullscreen(Edge));

        Assert.Equal(Placement.Fullscreen, ws.Find(Edge)!.Placement);
        Assert.Equal(Placement.InPanel, ws.Find(Ide)!.Placement); // el foco anterior vuelve a su panel
        Assert.Equal([Ide], ws.CenterHistory);
        Assert.Null(ws.Composition.Fullscreen!.Display);
    }

    [Fact]
    public void ExitingReturnsItToTheFocus()
    {
        var ws = WithTwo();
        ws.Execute(new EnterFullscreen(Ide));

        var outcome = ws.Execute(new ExitFullscreen(Ide));

        Assert.True(outcome.Accepted);
        Assert.Equal(Placement.Center, ws.Find(Ide)!.Placement);
        Assert.Null(ws.Composition.Fullscreen);
        Assert.Equal((nint)Ide, ws.Composition.Center);
        Assert.False(ws.Execute(new ExitFullscreen(Ide)).Accepted);
    }

    [Fact]
    public void PromotingAnotherEndsTheFullscreenLikeTheCenter()
    {
        var ws = WithTwo();
        ws.Execute(new EnterFullscreen(Ide));

        ws.Execute(new Promote(Edge));

        Assert.Equal(Placement.InPanel, ws.Find(Ide)!.Placement);
        Assert.Equal(Placement.Center, ws.Find(Edge)!.Placement);
        Assert.Null(ws.Composition.Fullscreen);

        ws.Execute(new SwapWithPrevious());
        Assert.Equal(Placement.Center, ws.Find(Ide)!.Placement);
    }

    [Fact]
    public void UnmaximizingItExitsFullscreenAndMinimizingParksIt()
    {
        var ws = WithTwo();
        ws.Execute(new EnterFullscreen(Ide));

        Assert.Equal([new ExitFullscreen(Ide)], ws.Interpret(Ide, ExternalChange.Unmaximized));
        Assert.Equal([new Park(Ide)], ws.Interpret(Ide, ExternalChange.Minimized));
        Assert.True(ws.Execute(new Park(Ide)).Accepted);
        Assert.Equal(Placement.Parked, ws.Find(Ide)!.Placement);
        Assert.Null(ws.Composition.Fullscreen);
    }

    [Fact]
    public void ReturnToPanelAlsoWorksFromFullscreen()
    {
        var ws = WithTwo();
        ws.Execute(new EnterFullscreen(Ide));

        Assert.True(ws.Execute(new ReturnToPanel(Ide)).Accepted);
        Assert.Equal(Placement.InPanel, ws.Find(Ide)!.Placement);
        Assert.Null(ws.Focused);
    }

    [Fact]
    public void IfItsMonitorIsGoneItGoesFullscreenOnThePrincipal()
    {
        var ws = WithTwo();
        ws.Execute(new EnterFullscreen(Ide, Aux));

        ws.Execute(new DisplayRemoved(Aux));

        Assert.Null(ws.Composition.Fullscreen!.Display);
    }
}
