using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

public class SceneTests
{
    private static SceneWindow W(string app, string title, Home home, ScenePlacement placement = ScenePlacement.InPanel, int? aux = null) =>
        new(app, app, title, home, placement, aux);

    private static readonly SavedScene Code = new(
        "Código",
        [
            W("slack.exe", "RPDev - Slack", Home.Context),
            W("msedge.exe", "Docs", Home.Work, ScenePlacement.Center),
            W("explorer.exe", "Home", Home.Work),
            W("githubdesktop.exe", "GitHub Desktop", Home.Work, ScenePlacement.Parked),
            W("chatgpt.exe", "ChatGPT", Home.Work, aux: 1),
        ],
        [new ScenePanelTemplate(null, Home.Work, PanelTemplate.Stack), new ScenePanelTemplate(1, Home.Work, PanelTemplate.Fill)]);

    // --- Emparejado ---------------------------------------------------------------------------

    [Fact]
    public void MatchesByAppAndReportsMissing()
    {
        var open = new List<OpenWindow>
        {
            new(10, "msedge.exe", "Docs - Edge"),
            new(11, "Slack.exe", "RPDev - Slack"),
            new(12, "explorer.exe", "Home - File Explorer"),
        };

        var m = SceneMatcher.Match(Code, open);

        Assert.Equal([11, 10, 12], m.Matched.Select(x => x.Window.Handle));
        Assert.Equal(["githubdesktop.exe", "chatgpt.exe"], m.Missing.Select(x => x.AppKey));
    }

    [Fact]
    public void WithSeveralWindowsOfTheSameAppPrefersTheTitle()
    {
        var scene = new SavedScene("Docs", [W("msedge.exe", "Jira", Home.Context), W("msedge.exe", "Docs", Home.Work, ScenePlacement.Center)], []);
        var open = new List<OpenWindow>
        {
            new(1, "msedge.exe", "Docs"),
            new(2, "msedge.exe", "Raid - List - Jira"),
        };

        var m = SceneMatcher.Match(scene, open);

        Assert.Equal(2, m.Matched[0].Window.Handle); // la pista "Jira" está contenida en el título
        Assert.Equal(1, m.Matched[1].Window.Handle); // título exacto
    }

    [Fact]
    public void ExactTitleWinsEvenIfAVagueHintComesFirst()
    {
        var scene = new SavedScene("x", [W("msedge.exe", "", Home.Context), W("msedge.exe", "Docs", Home.Work)], []);
        var open = new List<OpenWindow> { new(1, "msedge.exe", "Docs"), new(2, "msedge.exe", "Otra") };

        var m = SceneMatcher.Match(scene, open);

        Assert.Equal(2, m.Matched.Single(x => x.Entry.TitleHint == "").Window.Handle);
        Assert.Equal(1, m.Matched.Single(x => x.Entry.TitleHint == "Docs").Window.Handle);
    }

    [Fact]
    public void EachOpenWindowIsUsedOnce()
    {
        var scene = new SavedScene("x", [W("explorer.exe", "A", Home.Work), W("explorer.exe", "B", Home.Work)], []);

        var m = SceneMatcher.Match(scene, [new OpenWindow(1, "explorer.exe", "Home")]);

        Assert.Single(m.Matched);
        Assert.Single(m.Missing);
    }

    // --- ApplyScene -----------------------------------------------------------------------------

    private static ApplyScene Apply(params SceneEntry[] entries) =>
        new(entries, [(null, Home.Work, PanelTemplate.Stack), ("aux", Home.Work, PanelTemplate.Fill)]);

    [Fact]
    public void ApplySceneBuildsTheWholeModelAtOnce()
    {
        var ws = new Workspace();

        var outcome = ws.Execute(Apply(
            new SceneEntry(1, "Slack", Home.Context, ScenePlacement.InPanel, null),
            new SceneEntry(2, "Edge", Home.Work, ScenePlacement.Center, null),
            new SceneEntry(3, "Explorer", Home.Work, ScenePlacement.InPanel, null),
            new SceneEntry(4, "GitHub", Home.Work, ScenePlacement.Parked, null),
            new SceneEntry(5, "ChatGPT", Home.Work, ScenePlacement.InPanel, "aux")));

        var c = ws.Composition;
        Assert.Equal([1], c.Context);
        Assert.Equal(2, c.Center);
        Assert.Equal([3], c.Work);
        Assert.Equal([4], c.Parked);
        Assert.Equal([5], c.Aux.Single().Work);
        Assert.Equal(PanelTemplate.Stack, c.WorkTemplate);
        Assert.Equal(PanelTemplate.Fill, c.Aux.Single().WorkTemplate);
        Assert.Equal(5, outcome.Effects.OfType<CaptureOriginal>().Count());
        Assert.Contains(new Activate(2), outcome.Effects);
    }

    [Fact]
    public void SwitchingScenesReleasesWindowsThatAreNotInTheNewOne()
    {
        var ws = new Workspace();
        ws.Execute(Apply(
            new SceneEntry(1, "Slack", Home.Context, ScenePlacement.InPanel, null),
            new SceneEntry(2, "Edge", Home.Work, ScenePlacement.Center, null)));

        var outcome = ws.Execute(new ApplyScene(
            [new SceneEntry(2, "Edge", Home.Context, ScenePlacement.InPanel, null), new SceneEntry(3, "Explorer", Home.Work, ScenePlacement.Center, null)],
            []));

        Assert.Contains(new RestoreOriginal(1), outcome.Effects);
        Assert.DoesNotContain(new CaptureOriginal(2), outcome.Effects); // ya estaba en el diario
        Assert.Contains(new CaptureOriginal(3), outcome.Effects);
        Assert.False(ws.IsManaged(1));
        Assert.Equal(3, ws.Composition.Center);
        Assert.Equal([2], ws.Composition.Context);
        Assert.Equal(PanelTemplate.Tiles, ws.Composition.WorkTemplate); // los templates también cambian
        Assert.Equal([2], ws.CenterHistory); // "Volver a la anterior" lleva a Edge
    }

    [Fact]
    public void FillIsIgnoredOnThePrincipal()
    {
        var ws = new Workspace();

        ws.Execute(new ApplyScene([new SceneEntry(1, "A", Home.Work, ScenePlacement.InPanel, null)], [(null, Home.Work, PanelTemplate.Fill)]));

        Assert.Equal(PanelTemplate.Tiles, ws.Composition.WorkTemplate);
    }

    [Fact]
    public void OnlyOneCenter()
    {
        var ws = new Workspace();

        ws.Execute(Apply(
            new SceneEntry(1, "A", Home.Work, ScenePlacement.Center, null),
            new SceneEntry(2, "B", Home.Work, ScenePlacement.Center, null)));

        Assert.Equal(1, ws.Composition.Center);
        Assert.Equal([2], ws.Composition.Work);
    }
}
