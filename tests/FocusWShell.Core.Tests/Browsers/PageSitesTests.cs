using FocusWShell.Core.Browsers;

namespace FocusWShell.Core.Tests.Browsers;

public class PageSitesTests
{
    private static string[] Assign(params string[] titles) => [.. PageSites.Assign(titles).Select(s => s ?? "·")];

    [Fact]
    public void RepeatedEndingIsTheSite()
    {
        var sites = Assign("Board - RM board - Jira", "Backlog - Innovate - Jira", "Valimail");

        Assert.Equal(["Jira", "Jira", "·"], sites);
    }

    [Fact]
    public void AnEndingThatDoesNotRepeatLeavesThePageLoose() =>
        Assert.Equal(["·", "·"], Assign("Free SPF Checker - Instant Results", "Calendar - Week of November 8, 2026"));

    [Fact]
    public void RepeatedBeginningIsTheSite() =>
        Assert.Equal(["Dashboard", "Dashboard"], Assign("Dashboard · Sales", "Dashboard · Operations"));

    [Fact]
    public void AHomePageNamedLikeTheSiteJoinsIt() =>
        Assert.Equal(["YouTube", "YouTube", "YouTube"], Assign("Video one - YouTube", "Video two - YouTube", "YouTube"));

    [Fact]
    public void AnEndingThatStartsWithTheSiteJoinsIt() =>
        Assert.Equal(["OVHcloud", "OVHcloud", "OVHcloud"], Assign("Servers - OVHcloud", "Billing - OVHcloud", "Getting started - OVHcloud Documentation"));

    [Fact]
    public void AnAddressWithTheSiteNameJoinsIt() =>
        Assert.Equal(["Acme Parking", "Acme Parking", "Acme Parking"], Assign("Lots - Acme Parking", "Reports - Acme Parking", "spva.acmeparking.com/lots?id=1"));

    [Fact]
    public void AnAddressInsideALongerTitleDoesNotCount() =>
        Assert.Equal(["Acme Parking", "Acme Parking", "·"], Assign("Lots - Acme Parking", "Reports - Acme Parking", "acmeparking.com - Calendar - Week of May 4"));

    [Fact]
    public void AnAddressAndAnEndingThatShareTheName() =>
        Assert.Equal(["Hikvision", "Hikvision"], Assign("www.hikvision.com", "HiTools Delivery - HiTools - Hikvision Global"));

    [Fact]
    public void RepositoriesAreGitHub() =>
        Assert.Equal(["GitHub", "GitHub"], Assign("Fix login by ana · Pull Request #15 · acme/web", "acme/api: The API server"));

    [Fact]
    public void MailWithAnAddressIsGmail() =>
        Assert.Equal(["Gmail", "Gmail"], Assign("Your update is ready - ana@example.com - Gmail", "Inbox (2) - ana@acme.com - acme.com Mail"));

    [Fact]
    public void ASiteWithOnePageIsLoose() =>
        Assert.Equal(["·", "·"], Assign("Your update is ready - ana@example.com - Gmail", "Fix login · Pull Request #15 · acme/web"));

    [Theory]
    [InlineData("Board - RM board - Jira", "Jira", "Board - RM board")]
    [InlineData("Dashboard · Sales", "Dashboard", "Sales")]
    [InlineData("YouTube", "YouTube", "YouTube")]
    [InlineData("www.hikvision.com", "Hikvision", "www.hikvision.com")]
    public void ShortTitleDropsTheSite(string title, string site, string expected) =>
        Assert.Equal(expected, PageSites.ShortTitle(title, site));
}
