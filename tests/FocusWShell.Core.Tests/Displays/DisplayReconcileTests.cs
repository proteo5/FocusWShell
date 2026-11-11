using FocusWShell.Core.Displays;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Tests.Displays;

/// <summary>Monitores que van y vienen, incluido el principal (S6 y M2).</summary>
public class DisplayReconcileTests
{
    private const string Wide = "ultrawide";
    private const string Hp = "hp";
    private const string Tablet = "tablet";

    private static DisplayInfo Display(string key, int left, int width, bool primary) =>
        new(left, $@"\\.\{key}", new PixelRect(left, 0, left + width, 1080), new PixelRect(left, 0, left + width, 1040), primary, 96) { StableId = key };

    private static readonly DisplayInfo WideP = Display(Wide, 0, 3440, true);
    private static readonly DisplayInfo HpAux = Display(Hp, 3440, 1920, false);
    private static readonly DisplayInfo HpP = Display(Hp, 3440, 1920, true);
    private static readonly DisplayInfo TabletAux = Display(Tablet, 5360, 1280, false);

    private static DisplayReconcile.Plan Compute(IReadOnlyList<DisplayInfo> displays, string? previous, params string[] missing) =>
        DisplayReconcile.Compute([Hp, Tablet], d => missing.Contains(d), displays, previous);

    [Fact]
    public void NothingChangedNothingToTell()
    {
        var plan = Compute([WideP, HpAux, TabletAux], Wide);

        Assert.Empty(plan.Events);
        Assert.Equal(Wide, plan.Principal);
        Assert.Equal([new DisplayAdded(Hp), new DisplayAdded(Tablet)], plan.Commands);
    }

    [Fact]
    public void AuxiliaryLostAndReturned()
    {
        var lost = Compute([WideP, HpAux], Wide);
        Assert.Equal([new DisplayLost(Tablet)], lost.Events);
        Assert.Contains(new DisplayRemoved(Tablet), lost.Commands);

        var back = Compute([WideP, HpAux, TabletAux], Wide, Tablet);
        Assert.Equal([new DisplayReturned(Tablet)], back.Events);
    }

    [Fact]
    public void LosingThePrincipalMakesTheNewOneTakeTheCenterAndItsPanelsJoinThePrincipal()
    {
        // Se desconecta el ultrawide: Windows nombra principal al HP.
        var plan = Compute([HpP, TabletAux], Wide);

        Assert.Equal(Hp, plan.Principal);
        Assert.Equal([new PrincipalChanged(Wide, Hp, PreviousConnected: false)], plan.Events);
        Assert.Contains(new DisplayRemoved(Hp), plan.Commands); // sus paneles se juntan con los del principal
        Assert.Contains(new DisplayAdded(Tablet), plan.Commands);
    }

    [Fact]
    public void ThePrincipalComingBackGivesTheOtherItsPanelsBack()
    {
        var plan = Compute([WideP, HpAux, TabletAux], Hp, Hp);

        Assert.Equal(Wide, plan.Principal);
        Assert.Equal([new PrincipalChanged(Hp, Wide, PreviousConnected: true)], plan.Events);
        Assert.Contains(new DisplayAdded(Hp), plan.Commands);
    }

    [Fact]
    public void FirstTimeThereIsNoPrincipalChange()
    {
        var plan = Compute([HpP], previous: null);

        Assert.DoesNotContain(plan.Events, e => e is PrincipalChanged);
        Assert.Equal(Hp, plan.Principal);
    }

    [Fact]
    public void LosingThePrincipalStillReportsOtherLostAuxiliaries()
    {
        var plan = Compute([HpP], Wide);

        Assert.Equal([new PrincipalChanged(Wide, Hp, false), new DisplayLost(Tablet)], plan.Events);
    }
}
