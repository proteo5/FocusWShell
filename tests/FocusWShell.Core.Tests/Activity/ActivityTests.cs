using FocusWShell.Core.Activity;

namespace FocusWShell.Core.Tests.Activity;

public class ActivityTests
{
    private static readonly ActivityApp Code = new("code", "VS Code", "work");
    private static readonly ActivityApp Slack = new("slack", "Slack", "context");
    private static readonly ActivityApp Docs = new("docs", "Docs", "work");
    private static readonly DateTime T0 = new(2026, 11, 17, 10, 0, 0);

    private static DateTime At(int minutes) => T0.AddMinutes(minutes);

    [Fact]
    public void ASegmentClosesWhenTheInUseAppChanges()
    {
        var t = new ActivityTracker();
        t.Active(At(0));
        Assert.Empty(t.Observe(At(0), Code, Code));

        var closed = t.Observe(At(10), Code, Slack);

        Assert.Equal([new ActivitySegment(At(0), At(10), Code, Code)], closed);
        Assert.Equal([new ActivitySegment(At(10), At(25), Code, Slack)], t.Flush(At(25)));
    }

    [Fact]
    public void TheSameAppsDoNotCutTheSegment()
    {
        var t = new ActivityTracker();
        t.Active(At(0));
        t.Observe(At(0), Code, Slack);

        Assert.Empty(t.Observe(At(5), Code with { PanelId = "other" }, Slack));
        Assert.Single(t.Flush(At(10)));
    }

    [Fact]
    public void IdleClosesAtTheLastInputAndActiveStartsAgain()
    {
        var t = new ActivityTracker();
        t.Active(At(0));
        t.Observe(At(0), Code, Code);

        var closed = t.Idle(At(12)); // detectado a los 15, la última entrada fue a los 12
        Assert.Equal([new ActivitySegment(At(0), At(12), Code, Code)], closed);

        Assert.Empty(t.Observe(At(20), Docs, Docs)); // inactivo: no abre tramo
        Assert.False(t.Running);

        t.Active(At(30));
        Assert.Equal([new ActivitySegment(At(30), At(40), Docs, Docs)], t.Flush(At(40)));
    }

    [Fact]
    public void ASegmentIsSplitAtMidnight()
    {
        var t = new ActivityTracker();
        var night = new DateTime(2026, 11, 17, 23, 50, 0);
        t.Active(night);
        t.Observe(night, Code, Code);

        var closed = t.Flush(night.AddMinutes(20));

        Assert.Equal(2, closed.Count);
        Assert.Equal(new DateTime(2026, 11, 18), closed[0].End);
        Assert.Equal(new DateTime(2026, 11, 18), closed[1].Start);
    }

    [Fact]
    public void VeryShortSegmentsAreNotKept()
    {
        var t = new ActivityTracker();
        t.Active(At(0));
        t.Observe(At(0), Code, Code);

        Assert.Empty(t.Observe(At(0).AddMilliseconds(400), Code, Slack));
    }

    [Fact]
    public void TheDayCountsFocusInUseAndSideBySide()
    {
        var day = ActivityDay.From(
        [
            new(At(0), At(30), Code, Code),
            new(At(30), At(50), Code, Slack),   // en paralelo
            new(At(50), At(60), Docs, Docs),
            new(At(70), At(80), Code, Code),    // tras una pausa
        ]);

        Assert.Equal(TimeSpan.FromMinutes(70), day.Active);
        Assert.Equal(TimeSpan.FromMinutes(20), day.SideBySide);
        Assert.Equal(3, day.ToFocus); // Code, Docs, Code
        Assert.Equal(TimeSpan.FromMinutes(50), day.LongestFocus);
        Assert.Equal("VS Code", day.LongestFocusApp);

        var code = day.Apps[0];
        Assert.Equal("code", code.Key);
        Assert.Equal(TimeSpan.FromMinutes(60), code.InFocus);
        Assert.Equal(TimeSpan.FromMinutes(40), code.InUse);
        Assert.Equal(TimeSpan.FromMinutes(20), day.Apps.Single(a => a.Key == "slack").InUse);
        Assert.Equal(TimeSpan.Zero, day.Apps.Single(a => a.Key == "slack").InFocus);
    }

    [Fact]
    public void TheSameAppAfterAPauseIsNotANewArrivalButBreaksTheStreak()
    {
        var day = ActivityDay.From(
        [
            new(At(0), At(20), Code, Code),
            new(At(30), At(40), Code, Code),
        ]);

        Assert.Equal(1, day.ToFocus);
        Assert.Equal(TimeSpan.FromMinutes(20), day.LongestFocus);
    }
}
