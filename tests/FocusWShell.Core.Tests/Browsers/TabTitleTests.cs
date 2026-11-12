using FocusWShell.Core.Browsers;

namespace FocusWShell.Core.Tests.Browsers;

public class TabTitleTests
{
    [Theory]
    [InlineData("Inbox (2) - alfredo@example.com - Mail", "Inbox (2) - alfredo@example.com - Mail")]
    [InlineData("Reports - Reliant Parking - Memory usage - 333 MB", "Reports - Reliant Parking")]
    [InlineData("FWS-prueba-1 - Memory usage - 24.4 MB", "FWS-prueba-1")]
    [InlineData("clerk.openrouter.ai - Network error", "clerk.openrouter.ai")]
    [InlineData("Docs - Sleeping - Memory usage - 1,2 GB", "Docs")]
    [InlineData("Mail - High memory usage - 890 MB", "Mail")]
    public void RemovesStatesAndMemory(string name, string expected) =>
        Assert.Equal(expected, TabTitle.Clean(name).Title);

    [Fact]
    public void ExtractsTheGroup()
    {
        var clean = TabTitle.Clean("DeepSeek - Part of tab group IA - Sleeping - Memory usage - 153 MB");

        Assert.Equal("DeepSeek", clean.Title);
        Assert.Equal("IA", clean.Group);
    }

    [Fact]
    public void KeepsHyphensThatArePartOfTheTitle()
    {
        var clean = TabTitle.Clean("Board - RM board - Jira");

        Assert.Equal("Board - RM board - Jira", clean.Title);
        Assert.Null(clean.Group);
    }

    [Fact]
    public void NeverReturnsAnEmptyTitle() =>
        Assert.Equal("Sleeping", TabTitle.Clean("Sleeping").Title);
}
