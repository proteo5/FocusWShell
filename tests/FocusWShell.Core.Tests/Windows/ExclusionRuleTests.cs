using FocusWShell.Core.Windows;

namespace FocusWShell.Core.Tests.Windows;

public class ExclusionRuleTests
{
    private static WindowSnapshot Window(string process = "NetBird.exe", string cls = "Qt5QWindowIcon", string? aumid = null) => new()
    {
        Handle = 1,
        Title = "NetBird",
        ClassName = cls,
        ProcessId = 10,
        ProcessName = process,
        AppUserModelId = aumid,
        IsVisible = true,
        HasCaption = true,
    };

    [Theory]
    [InlineData("NetBird.exe")]
    [InlineData("netbird")]
    [InlineData(" NETBIRD.EXE ")]
    public void ExecutableMatchesWithOrWithoutExtensionAndIgnoringCase(string value) =>
        Assert.True(new ExclusionRule(ExclusionKind.Executable, value).Matches(Window()));

    [Fact]
    public void ExecutableDoesNotMatchAnotherApp() =>
        Assert.False(new ExclusionRule(ExclusionKind.Executable, "Code.exe").Matches(Window()));

    [Fact]
    public void ClassMatchesOnlyThatClass()
    {
        var rule = new ExclusionRule(ExclusionKind.WindowClass, "qt5qwindowicon");
        Assert.True(rule.Matches(Window()));
        Assert.False(rule.Matches(Window(cls: "Chrome_WidgetWin_1")));
    }

    [Fact]
    public void AumidNeedsAPackagedApp()
    {
        var rule = new ExclusionRule(ExclusionKind.Aumid, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.True(rule.Matches(Window(aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")));
        Assert.False(rule.Matches(Window()));
    }

    [Fact]
    public void EmptyRuleNeverMatches() =>
        Assert.False(new ExclusionRule(ExclusionKind.Executable, "  ").Matches(Window(process: "")));

    [Fact]
    public void ForAppPrefersTheAumid()
    {
        Assert.Equal(new ExclusionRule(ExclusionKind.Aumid, "Pkg!App"), ExclusionRule.ForApp(Window(aumid: "Pkg!App")));
        Assert.Equal(new ExclusionRule(ExclusionKind.Executable, "NetBird.exe"), ExclusionRule.ForApp(Window()));
        Assert.Null(ExclusionRule.ForApp(Window(process: "")));
    }

    [Fact]
    public void SameAsIgnoresExtensionAndCase()
    {
        Assert.True(new ExclusionRule(ExclusionKind.Executable, "NetBird.exe").SameAs(new ExclusionRule(ExclusionKind.Executable, "netbird")));
        Assert.False(new ExclusionRule(ExclusionKind.Executable, "NetBird").SameAs(new ExclusionRule(ExclusionKind.WindowClass, "NetBird")));
    }

    [Fact]
    public void FilterReportsUserExcluded()
    {
        var rules = new[] { new ExclusionRule(ExclusionKind.Executable, "netbird") };
        var filter = new EligibilityFilter(minVisibleSize: 0, userExcluded: w => ExclusionRule.AnyMatches(rules, w));
        var w = Window() with { VisibleBounds = new FocusWShell.Core.Geometry.PixelRect(0, 0, 400, 600), Bounds = new FocusWShell.Core.Geometry.PixelRect(0, 0, 400, 600) };

        Assert.Equal(ExclusionReason.UserExcluded, filter.Evaluate(w, IntegrityLevel.Medium).Reason);
    }
}
