using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Domain;

public class SceneTests
{
    private const string Ctx = Workspace.ContextPanel;
    private const string Wrk = Workspace.WorkPanel;

    private static SceneWindow W(string app, string title, string panel, ScenePlacement placement = ScenePlacement.InPanel) =>
        new(app, app, title, panel, placement);

    // --- Emparejado ---------------------------------------------------------------------------

    private static readonly SavedScene Code = new(
        "Código",
        [
            W("slack.exe", "RPDev - Slack", Ctx),
            W("msedge.exe", "Docs", Wrk, ScenePlacement.Center),
            W("explorer.exe", "Home", Wrk),
            W("githubdesktop.exe", "GitHub Desktop", Wrk, ScenePlacement.Parked),
            W("chatgpt.exe", "ChatGPT", "aux1-trabajo"),
        ]);

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
        var scene = new SavedScene("Docs", [W("msedge.exe", "Jira", Ctx), W("msedge.exe", "Docs", Wrk, ScenePlacement.Center)]);
        var open = new List<OpenWindow> { new(1, "msedge.exe", "Docs"), new(2, "msedge.exe", "Raid - List - Jira") };

        var m = SceneMatcher.Match(scene, open);

        Assert.Equal(2, m.Matched[0].Window.Handle); // la pista "Jira" está contenida en el título
        Assert.Equal(1, m.Matched[1].Window.Handle); // título exacto
    }

    [Fact]
    public void ExactTitleWinsEvenIfAVagueHintComesFirst()
    {
        var scene = new SavedScene("x", [W("msedge.exe", "", Ctx), W("msedge.exe", "Docs", Wrk)]);
        var open = new List<OpenWindow> { new(1, "msedge.exe", "Docs"), new(2, "msedge.exe", "Otra") };

        var m = SceneMatcher.Match(scene, open);

        Assert.Equal(2, m.Matched.Single(x => x.Entry.TitleHint == "").Window.Handle);
        Assert.Equal(1, m.Matched.Single(x => x.Entry.TitleHint == "Docs").Window.Handle);
    }

    [Fact]
    public void EachOpenWindowIsUsedOnce()
    {
        var scene = new SavedScene("x", [W("explorer.exe", "A", Wrk), W("explorer.exe", "B", Wrk)]);

        var m = SceneMatcher.Match(scene, [new OpenWindow(1, "explorer.exe", "Home")]);

        Assert.Single(m.Matched);
        Assert.Single(m.Missing);
    }

    // --- Migración del formato anterior --------------------------------------------------------

    [Fact]
    public void OldScenesAreUpgradedToPanels()
    {
        // Formato anterior: hogar Contexto/Trabajo, monitor auxiliar por orden y templates sueltos.
        var old = new SavedScene(
            "Código",
            [
                new SceneWindow("slack.exe", "Slack", "x", null, ScenePlacement.InPanel) { Home = "Context" },
                new SceneWindow("msedge.exe", "Edge", "x", null, ScenePlacement.Center) { Home = "Work" },
                new SceneWindow("chatgpt.exe", "ChatGPT", "x", null, ScenePlacement.InPanel) { Home = "Work", AuxIndex = 1 },
            ])
        {
            Templates = [new ScenePanelTemplate(null, "Work", PanelTemplate.Stack), new ScenePanelTemplate(1, "Work", PanelTemplate.Fill)],
        };

        var s = old.Upgrade();

        Assert.Equal([Ctx, Wrk, "aux1-trabajo"], s.Windows.Select(w => w.PanelId));
        Assert.All(s.Windows, w => Assert.Null(w.Home));
        Assert.Equal(PanelTemplate.Stack, s.Panels.Single(p => p.Id == Wrk).Template);
        var aux = s.Panels.Single(p => p.Id == "aux1-trabajo");
        Assert.Equal(1, aux.AuxIndex);
        Assert.Equal(PanelTemplate.Fill, aux.Template);
        Assert.Same(s, s.Upgrade()); // ya migrada: no cambia
    }

    // --- ApplyScene -----------------------------------------------------------------------------

    private static readonly IReadOnlyList<Panel> ScenePanels =
    [
        new(Ctx, "Contexto", null, PanelSide.Left),
        new(Wrk, "Trabajo", null, PanelSide.Right, PanelTemplate.Stack),
        new("aux-trabajo", "Monitoreo", "aux", PanelSide.Right, PanelTemplate.Fill),
    ];

    [Fact]
    public void ApplySceneBuildsTheWholeModelAtOnce()
    {
        var ws = new Workspace();

        var outcome = ws.Execute(new ApplyScene(
            [
                new SceneEntry(1, "Slack", Ctx, ScenePlacement.InPanel),
                new SceneEntry(2, "Edge", Wrk, ScenePlacement.Center),
                new SceneEntry(3, "Explorer", Wrk, ScenePlacement.InPanel),
                new SceneEntry(4, "GitHub", Wrk, ScenePlacement.Parked),
                new SceneEntry(5, "ChatGPT", "aux-trabajo", ScenePlacement.InPanel),
            ],
            ScenePanels));

        var c = ws.Composition;
        Assert.Equal([1], c.Context);
        Assert.Equal(2, c.Center);
        Assert.Equal([3], c.Work);
        Assert.Equal([4], c.Parked);
        Assert.Equal([5], c.Aux.Single().Work);
        Assert.Equal(PanelTemplate.Stack, c.WorkTemplate);
        Assert.Equal("Monitoreo", c.Aux.Single().Panels.Single().Panel.Name);
        Assert.Equal(5, outcome.Effects.OfType<CaptureOriginal>().Count());
        Assert.Contains(new Activate(2), outcome.Effects);
    }

    [Fact]
    public void SwitchingScenesReleasesWindowsThatAreNotInTheNewOne()
    {
        var ws = new Workspace();
        ws.Execute(new ApplyScene([new SceneEntry(1, "Slack", Ctx, ScenePlacement.InPanel), new SceneEntry(2, "Edge", Wrk, ScenePlacement.Center)], ScenePanels));

        var outcome = ws.Execute(new ApplyScene(
            [new SceneEntry(2, "Edge", Ctx, ScenePlacement.InPanel), new SceneEntry(3, "Explorer", Wrk, ScenePlacement.Center)],
            Workspace.DefaultPanels()));

        Assert.Contains(new RestoreOriginal(1), outcome.Effects);
        Assert.DoesNotContain(new CaptureOriginal(2), outcome.Effects); // ya estaba en el diario
        Assert.Contains(new CaptureOriginal(3), outcome.Effects);
        Assert.False(ws.IsManaged(1));
        Assert.Equal(3, ws.Composition.Center);
        Assert.Equal([2], ws.Composition.Context);
        Assert.Equal(PanelTemplate.Tiles, ws.Composition.WorkTemplate); // los paneles también cambian
        Assert.Equal([2], ws.CenterHistory); // "Volver a la anterior" lleva a Edge
    }

    [Fact]
    public void FillIsIgnoredOnThePrincipal()
    {
        var ws = new Workspace();

        ws.Execute(new ApplyScene(
            [new SceneEntry(1, "A", Wrk, ScenePlacement.InPanel)],
            [new Panel(Ctx, "Contexto", null, PanelSide.Left), new Panel(Wrk, "Trabajo", null, PanelSide.Right, PanelTemplate.Fill)]));

        Assert.Equal(PanelTemplate.Tiles, ws.Composition.WorkTemplate);
    }

    [Fact]
    public void WindowWithoutAResolvablePanelStaysFree()
    {
        // Una app sin panel asignado flota libre (ADR 0011): no se adopta.
        var ws = new Workspace();

        var outcome = ws.Execute(new ApplyScene([new SceneEntry(1, "A", "no-existe", ScenePlacement.InPanel)], []));

        Assert.False(ws.IsManaged(1));
        Assert.DoesNotContain(outcome.Effects, e => e is CaptureOriginal);
    }

    [Fact]
    public void ScenePanelsAddToTheUsersPanels()
    {
        var ws = new Workspace();
        ws.Execute(new RenamePanel(Ctx, "Comunicación"));
        ws.Execute(new CreatePanel(new Panel("mon", "Monitoreo", "HP", PanelSide.Right)));

        ws.Execute(new ApplyScene(
            [new SceneEntry(1, "Slack", Ctx, ScenePlacement.InPanel), new SceneEntry(2, "ChatGPT", "chat", ScenePlacement.InPanel)],
            [new Panel(Ctx, "Contexto", null, PanelSide.Left, PanelTemplate.Stack), new Panel("chat", "Chat", "HP", PanelSide.Left)]));

        Assert.Equal([Ctx, Wrk, "mon", "chat"], ws.Panels.Select(p => p.Id));
        var ctx = ws.FindPanel(Ctx)!;
        Assert.Equal("Comunicación", ctx.Name);              // conserva el nombre del usuario
        Assert.Equal(PanelTemplate.Stack, ctx.Template);      // toma la presentación de la escena
        Assert.Equal([2], ws.Composition.Aux.Single().Context);
    }

    [Fact]
    public void ScenePanelOnAnOccupiedPrincipalColumnUsesThePanelThatIsThere()
    {
        var ws = new Workspace();

        ws.Execute(new ApplyScene(
            [new SceneEntry(1, "Slack", "otro", ScenePlacement.InPanel)],
            [new Panel("otro", "Otro", null, PanelSide.Left)]));

        Assert.Equal([1], ws.Composition.Context);
        Assert.Null(ws.FindPanel("otro"));
    }

    [Fact]
    public void ReplacePanelsLoadsTheSavedOnesAtStartup()
    {
        var ws = new Workspace();

        var outcome = ws.Execute(new ReplacePanels(
        [
            new Panel(Ctx, "Comunicación", null, PanelSide.Left),
            new Panel("mon", "Monitoreo", "HP", PanelSide.Right, PanelTemplate.Fill),
        ]));

        Assert.True(outcome.Accepted);
        Assert.Equal([Ctx, "mon"], ws.Panels.Select(p => p.Id));
        Assert.Equal("Comunicación", ws.FindPanel(Ctx)!.Name);
        Assert.False(ws.Execute(new ReplacePanels([new Panel("x", "X", "HP", PanelSide.Left)])).Accepted); // sin principal
    }

    [Fact]
    public void OnlyOneCenter()
    {
        var ws = new Workspace();

        ws.Execute(new ApplyScene(
            [new SceneEntry(1, "A", Wrk, ScenePlacement.Center), new SceneEntry(2, "B", Wrk, ScenePlacement.Center)],
            []));

        Assert.Equal(1, ws.Composition.Center);
        Assert.Equal([2], ws.Composition.Work);
    }
}
