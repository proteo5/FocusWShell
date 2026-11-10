using FocusWShell.Core.Geometry;
using FocusWShell.Core.Windows;

namespace FocusWShell.Core.Tests.Windows;

public class EligibilityFilterTests
{
    private static readonly PixelRect Monitor = PixelRect.FromSize(0, 0, 3440, 1440);

    private static WindowSnapshot NormalWindow() => new()
    {
        Handle = 0x1234,
        Title = "Documento - Editor",
        ClassName = "Chrome_WidgetWin_1",
        ProcessId = 42,
        ProcessName = "Code.exe",
        IsVisible = true,
        HasCaption = true,
        IsResizable = true,
        Bounds = PixelRect.FromSize(93, 93, 1214, 807),
        VisibleBounds = PixelRect.FromSize(100, 100, 1200, 800),
        MonitorBounds = Monitor,
        Dpi = 96,
        Integrity = IntegrityLevel.Medium,
    };

    private readonly EligibilityFilter _filter = new();

    private Eligibility Evaluate(WindowSnapshot w, IntegrityLevel own = IntegrityLevel.Medium) =>
        _filter.Evaluate(w, own);

    [Fact]
    public void NormalAppWindowIsCandidateAndManageable()
    {
        var result = Evaluate(NormalWindow());

        Assert.True(result.IsCandidate);
        Assert.True(result.Manageable);
    }

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("#32770")]
    public void SystemClassesAreExcluded(string className)
    {
        var result = Evaluate(NormalWindow() with { ClassName = className });

        Assert.Equal(ExclusionReason.SystemClass, result.Reason);
        Assert.False(result.Manageable);
    }

    [Fact]
    public void OwnProcessIsExcludedFirst()
    {
        var result = Evaluate(NormalWindow() with { IsOwnProcess = true, IsVisible = false });

        Assert.Equal(ExclusionReason.OwnProcess, result.Reason);
    }

    [Fact]
    public void InvisibleWindowIsExcluded() =>
        Assert.Equal(ExclusionReason.NotVisible, Evaluate(NormalWindow() with { IsVisible = false }).Reason);

    [Fact]
    public void CloakedWindowIsExcluded() =>
        Assert.Equal(ExclusionReason.Cloaked, Evaluate(NormalWindow() with { IsCloaked = true }).Reason);

    [Fact]
    public void OwnedDialogIsExcluded() =>
        Assert.Equal(ExclusionReason.Owned, Evaluate(NormalWindow() with { HasOwner = true }).Reason);

    [Fact]
    public void OwnedWindowWithAppWindowStyleIsCandidate() =>
        Assert.True(Evaluate(NormalWindow() with { HasOwner = true, IsAppWindow = true }).IsCandidate);

    [Fact]
    public void ToolWindowIsExcluded() =>
        Assert.Equal(ExclusionReason.ToolWindow, Evaluate(NormalWindow() with { IsToolWindow = true }).Reason);

    [Fact]
    public void NoActivateWindowIsExcluded() =>
        Assert.Equal(ExclusionReason.NoActivate, Evaluate(NormalWindow() with { IsNoActivate = true }).Reason);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UntitledWindowIsExcluded(string title) =>
        Assert.Equal(ExclusionReason.NoTitle, Evaluate(NormalWindow() with { Title = title }).Reason);

    [Fact]
    public void TinyWindowIsExcluded()
    {
        var tiny = PixelRect.FromSize(0, 0, 80, 40);

        var result = Evaluate(NormalWindow() with { Bounds = tiny, VisibleBounds = tiny });

        Assert.Equal(ExclusionReason.TooSmall, result.Reason);
    }

    [Fact]
    public void MinimizedWindowIsCandidateDespiteTinyBounds()
    {
        var offscreen = PixelRect.FromSize(-32000, -32000, 160, 28);

        var result = Evaluate(NormalWindow() with { IsMinimized = true, Bounds = offscreen, VisibleBounds = offscreen });

        Assert.True(result.IsCandidate);
    }

    [Fact]
    public void BorderlessWindowCoveringMonitorIsFullscreen()
    {
        var result = Evaluate(NormalWindow() with
        {
            HasCaption = false,
            IsResizable = false,
            Bounds = Monitor,
            VisibleBounds = Monitor,
        });

        Assert.Equal(ExclusionReason.Fullscreen, result.Reason);
    }

    [Fact]
    public void ResizableWindowWithCustomTitleBarIsCandidate()
    {
        // Chromium/Electron/Terminal: sin WS_CAPTION pero con WS_THICKFRAME.
        var result = Evaluate(NormalWindow() with { HasCaption = false, IsResizable = true });

        Assert.True(result.IsCandidate);
    }

    [Fact]
    public void FixedBorderlessPopupIsExcluded()
    {
        var result = Evaluate(NormalWindow() with { HasCaption = false, IsResizable = false });

        Assert.Equal(ExclusionReason.NoCaption, result.Reason);
    }

    [Fact]
    public void UserExclusionIsApplied()
    {
        var filter = new EligibilityFilter(userExcluded: w => w.ProcessName == "Code.exe");

        var result = filter.Evaluate(NormalWindow(), IntegrityLevel.Medium);

        Assert.Equal(ExclusionReason.UserExcluded, result.Reason);
    }

    [Theory]
    [InlineData(IntegrityLevel.High, IntegrityLevel.Medium, false)]
    [InlineData(IntegrityLevel.System, IntegrityLevel.Medium, false)]
    [InlineData(IntegrityLevel.Unknown, IntegrityLevel.Medium, false)]
    [InlineData(IntegrityLevel.Medium, IntegrityLevel.Medium, true)]
    [InlineData(IntegrityLevel.Low, IntegrityLevel.Medium, true)]
    [InlineData(IntegrityLevel.High, IntegrityLevel.High, true)]
    public void ManageabilityFollowsIntegrity(IntegrityLevel target, IntegrityLevel own, bool expected)
    {
        var result = Evaluate(NormalWindow() with { Integrity = target }, own);

        Assert.True(result.IsCandidate);
        Assert.Equal(expected, result.Manageable);
    }
}
