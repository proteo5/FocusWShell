using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>El deck fijado en un monitor (ADR 0013, fase 4).</summary>
public class DeckDockTests
{
    // La tablet de la prueba: 1280×720 debajo del principal.
    private static readonly PixelRect Tablet = new(2154, 1440, 3434, 2112);

    [Fact]
    public void HalvesLeaveTheOtherHalfToThePanels()
    {
        var (deck, panels) = DeckDock.Split(Tablet, DeckZone.LeftHalf);

        Assert.Equal(new PixelRect(2154, 1440, 2794, 2112), deck);
        Assert.Equal(new PixelRect(2794, 1440, 3434, 2112), panels);
        Assert.Equal(new PixelRect(2794, 1440, 3434, 2112), DeckDock.Split(Tablet, DeckZone.RightHalf).Deck);
    }

    [Fact]
    public void TheWholeMonitorLeavesNothingForPanels()
    {
        var (deck, panels) = DeckDock.Split(Tablet, DeckZone.Whole);

        Assert.Equal(Tablet, deck);
        Assert.Null(panels);
    }

    [Fact]
    public void BesideTheDeckAFillPanelTakesTheRestWithItsTabsInsteadOfMaximizing()
    {
        var (_, rest) = DeckDock.Split(Tablet, DeckZone.LeftHalf);
        var r = AuxLayoutEngine.Compute(new AuxInput("tablet", rest!.Value,
        [
            new PanelInput("musica", PanelSide.Left, 1, PanelTemplate.Fill, [new LayoutWindow(1, "Spotify"), new LayoutWindow(2, "Podcasts")]),
        ], TopInset: 35) { NoMaximize = true });

        var spotify = r.Targets.Single(t => t.Handle == 1);
        Assert.Equal(TargetKind.Visible, spotify.Kind);
        Assert.Equal(new PixelRect(2794, 1440 + 35, 3434, 2112), spotify.Rect);
        Assert.Equal(TargetKind.Minimized, r.Targets.Single(t => t.Handle == 2).Kind);
        Assert.Equal(PanelTemplate.Stack, r.EffectiveTemplates["musica"]); // con tira de pestañas
        Assert.Equal(35, r.StripHeight);
        Assert.Equal(rest.Value, r.Bands["musica"]);
    }
}
