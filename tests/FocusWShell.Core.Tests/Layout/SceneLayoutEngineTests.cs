using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

public class SceneLayoutEngineTests
{
    // Área de trabajo del ultrawide medida en S2.
    private static readonly PixelRect Work = new(0, 0, 3440, 1392);

    private static LayoutWindow Win(int id, string label, int minW = 0, int minH = 0) =>
        new(id, label, minW == 0 && minH == 0 ? null : new PixelSize(minW, minH));

    private static SceneInput Scene(
        IReadOnlyList<LayoutWindow> context,
        LayoutWindow? center,
        IReadOnlyList<LayoutWindow> work,
        int gap = 8,
        PanelTemplate contextTemplate = PanelTemplate.Tiles,
        PanelTemplate workTemplate = PanelTemplate.Tiles) =>
        new(
            Work,
            new PanelInput("Contexto", PanelSide.Left, 18, contextTemplate, context),
            55,
            center,
            new PanelInput("Trabajo", PanelSide.Right, 27, workTemplate, work),
            gap);

    private static WindowTarget Target(SceneLayoutResult r, int id) => r.Targets.Single(t => t.Handle == id);

    /// <summary>Invariante: ninguna ventana de panel se superpone a la columna del Centro.</summary>
    private static void AssertCenterProtected(SceneLayoutResult r)
    {
        foreach (var t in r.Targets.Where(t => t.Region != "Centro" && t.Kind == TargetKind.Visible))
        {
            Assert.True(t.Rect.Intersect(r.CenterColumn).IsEmpty, $"{t.Label} invade el Centro: {t.Rect} ∩ {r.CenterColumn}");
        }
    }

    [Fact]
    public void PanelTopInsetLeavesRoomForTheTabStripButNotInTheCenter()
    {
        var input = Scene([Win(1, "Chat")], Win(2, "Editor"), [Win(3, "Terminal"), Win(4, "Logs")]) with { PanelTopInset = 28 };

        var r = SceneLayoutEngine.Compute(input);

        Assert.Equal(28, r.LeftColumn.Top);
        Assert.Equal(28, r.RightColumn.Top);
        Assert.Equal(0, r.CenterColumn.Top);
        Assert.Equal(28, Target(r, 1).Rect.Top);
        Assert.Equal(28, Target(r, 3).Rect.Top);
        Assert.Equal(Work.Bottom, Target(r, 4).Rect.Bottom);
        Assert.Equal(new PixelRect(r.CenterColumn.Left, 0, r.CenterColumn.Right, Work.Bottom), Target(r, 2).Rect);
    }

    [Fact]
    public void WithoutMinimumsMatchesTheWeights()
    {
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Chat")], Win(2, "Editor"), [Win(3, "Terminal")]));
        var ideal = Splitter.Columns(Work, [18, 55, 27], 8);

        Assert.Equal(ideal[0], r.LeftColumn);
        Assert.Equal(ideal[1], r.CenterColumn);
        Assert.Equal(ideal[2], r.RightColumn);
        Assert.Equal(r.CenterColumn, Target(r, 2).Rect);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void SlackLikeMinimumWidensContextAtExpenseOfCenter()
    {
        // S2: Slack exige 670 px y Contexto mide 616.
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Slack", 670), Win(2, "WhatsApp")], Win(3, "Edge"), []));

        Assert.Equal(670, r.LeftColumn.Width);
        Assert.Equal(new PixelRect(0, 0, 670, 692), Target(r, 1).Rect);
        Assert.Equal(670, Target(r, 2).Rect.Width);
        Assert.Equal(r.LeftColumn.Right + 8, r.CenterColumn.Left);
        Assert.Equal(1884 - 54, r.CenterColumn.Width);
        AssertCenterProtected(r);
        Assert.Contains(r.Notes, n => n.Contains("Slack", StringComparison.Ordinal));
    }

    [Fact]
    public void BothPanelsGrowAndCenterStaysAboveFloor()
    {
        // S2: Slack 670 a la izquierda y GitHub Desktop 962x661 a la derecha.
        var r = SceneLayoutEngine.Compute(Scene(
            [Win(1, "Slack", 670), Win(2, "WhatsApp")],
            Win(3, "Edge"),
            [Win(4, "Explorer"), Win(5, "SSMS"), Win(6, "GitHub Desktop", 962, 661)]));

        Assert.Equal(670, r.LeftColumn.Width);
        Assert.Equal(962, r.RightColumn.Width);
        Assert.True(r.CenterColumn.Width >= 1720);
        Assert.Equal(Work.Right, r.RightColumn.Right);
        Assert.Equal(PanelTemplate.Tiles, r.EffectiveTemplates["Trabajo"]);
        Assert.True(Target(r, 6).Rect.Height >= 661);
        Assert.Equal(Work.Bottom, r.Targets.Where(t => t.Region == "Trabajo").Max(t => t.Rect.Bottom));
        AssertCenterProtected(r);
    }

    [Fact]
    public void WhenCenterFloorIsReachedWindowOverflowsOutwardNotIntoCenter()
    {
        // S5: Docker Desktop exige 942 px en Contexto. El Centro no baja de 1720 (50 %).
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Docker", 942)], Win(2, "Editor"), [Win(3, "Explorer", 1200)]));

        Assert.Equal(1720, r.CenterColumn.Width);
        var docker = Target(r, 1).Rect;
        Assert.Equal(942, docker.Width);
        Assert.Equal(r.LeftColumn.Right, docker.Right);
        Assert.True(docker.Left < Work.Left, "el exceso debe quedar fuera de pantalla a la izquierda");
        var explorer = Target(r, 3).Rect;
        Assert.Equal(r.RightColumn.Left, explorer.Left);
        Assert.True(explorer.Right > Work.Right, "el exceso debe quedar fuera de pantalla a la derecha");
        AssertCenterProtected(r);
        Assert.Contains(r.Notes, n => n.Contains("fuera de pantalla", StringComparison.Ordinal));
    }

    [Fact]
    public void TilesThatDoNotFitInHeightPageByRows()
    {
        var windows = new[] { Win(2, "GitHub Desktop", 900, 700), Win(3, "Docker", 900, 720), Win(4, "Terminal") };
        var r = SceneLayoutEngine.Compute(Scene([], Win(1, "Editor"), windows));

        // Ya no pasan a pestañas (ADR 0015): se ven las filas que caben y las demás quedan ocultas.
        Assert.Equal(PanelTemplate.Tiles, r.EffectiveTemplates["Trabajo"]);
        Assert.Equal(TargetKind.Visible, Target(r, 2).Kind);
        Assert.Equal(r.RightColumn.Height, Target(r, 2).Rect.Height);
        Assert.Equal(TargetKind.Minimized, Target(r, 3).Kind);
        Assert.Equal(TargetKind.Minimized, Target(r, 4).Kind);
        Assert.Equal(new TilePages(0, 1, 3), r.Pages["Trabajo"]);
        Assert.Contains(r.Notes, n => n.Contains("ocupan 3 filas", StringComparison.Ordinal));
    }

    [Fact]
    public void ScrollingAndRevealingChooseTheVisibleRows()
    {
        var windows = new[] { Win(2, "GitHub Desktop", 900, 700), Win(3, "Docker", 900, 720), Win(4, "Terminal") };
        var scene = Scene([], Win(1, "Editor"), windows);

        var scrolled = SceneLayoutEngine.Compute(scene with { Right = scene.Right with { ScrollRow = 1 } });
        var revealed = SceneLayoutEngine.Compute(scene with { Right = scene.Right with { Reveal = 4 } });

        // Desde la fila 2 caben Docker y Terminal (720 + 0): se ven las dos.
        Assert.Equal(new TilePages(1, 2, 3), scrolled.Pages["Trabajo"]);
        Assert.Equal(TargetKind.Minimized, Target(scrolled, 2).Kind);
        Assert.Equal(TargetKind.Visible, Target(scrolled, 4).Kind);
        Assert.Equal(new TilePages(1, 2, 3), revealed.Pages["Trabajo"]);
    }

    [Fact]
    public void StackTemplateShowsOnlyFirstWindow()
    {
        var r = SceneLayoutEngine.Compute(Scene(
            [], null, [Win(1, "A"), Win(2, "B")], workTemplate: PanelTemplate.Stack));

        Assert.Equal(TargetKind.Visible, Target(r, 1).Kind);
        Assert.Equal(r.RightColumn, Target(r, 1).Rect);
        Assert.Equal(TargetKind.Minimized, Target(r, 2).Kind);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void StackWidthOnlyConsidersVisibleWindow()
    {
        // La ventana oculta exige mucho ancho, pero no está visible: no debe encoger el Centro.
        var r = SceneLayoutEngine.Compute(Scene(
            [], Win(1, "Editor"), [Win(2, "Terminal"), Win(3, "Ancha", 1500)], workTemplate: PanelTemplate.Stack));

        Assert.Equal(924, r.RightColumn.Width);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void WideCenterWindowTakesFromContextFirst()
    {
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Chat", 400)], Win(2, "Editor", 2000), [Win(3, "Terminal")]));

        Assert.Equal(2000, r.CenterColumn.Width);
        Assert.Equal(616 - 116, r.LeftColumn.Width);
        Assert.Equal(924, r.RightColumn.Width);
        Assert.Equal(r.CenterColumn, Target(r, 2).Rect);
        AssertCenterProtected(r);
    }

    [Fact]
    public void WideCenterWindowNeverShrinksPanelBelowItsWindowMinimum()
    {
        // El Centro pide 2300 px; los paneles sólo pueden ceder hasta 600 y 700.
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Chat", 600)], Win(2, "Editor", 2300), [Win(3, "Terminal", 700)]));

        Assert.Equal(600, r.LeftColumn.Width);
        Assert.Equal(700, r.RightColumn.Width);
        Assert.Equal(Work.Width - 600 - 700 - 16, r.CenterColumn.Width);
        AssertCenterProtected(r);

        // La ventana del Centro, que sigue sin caber, se centra sobre su columna.
        var editor = Target(r, 2).Rect;
        Assert.Equal(2300, editor.Width);
        Assert.Equal(r.CenterColumn.Left - ((2300 - r.CenterColumn.Width) / 2), editor.Left);
        Assert.Contains(r.Notes, n => n.Contains("se centra", StringComparison.Ordinal));
    }

    [Fact]
    public void WideCenterWindowTakesFromWorkWhenContextIsExhausted()
    {
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Chat", 600)], Win(2, "Editor", 2100), [Win(3, "Terminal", 700)]));

        Assert.Equal(600, r.LeftColumn.Width);
        Assert.Equal(2100, r.CenterColumn.Width);
        Assert.Equal(924 - 200, r.RightColumn.Width);
    }

    [Fact]
    public void ColumnsAlwaysSpanTheWorkArea()
    {
        var r = SceneLayoutEngine.Compute(Scene([Win(1, "Slack", 670)], Win(2, "Editor", 1900), [Win(3, "GH", 962, 661)]));

        Assert.Equal(Work.Left, r.LeftColumn.Left);
        Assert.Equal(8, r.CenterColumn.Left - r.LeftColumn.Right);
        Assert.Equal(8, r.RightColumn.Left - r.CenterColumn.Right);
        Assert.Equal(Work.Right, r.RightColumn.Right);
    }

    [Fact]
    public void RowsRespectMinimumsAndFillHeight()
    {
        var rows = SceneLayoutEngine.DistributeRows(new PixelRect(0, 0, 900, 1392), [0, 0, 661], 8);

        Assert.Equal(661, rows[2].Height);
        Assert.Equal(0, rows[0].Top);
        Assert.Equal(1392, rows[2].Bottom);
        Assert.Equal(8, rows[1].Top - rows[0].Bottom);
        Assert.True(Math.Abs(rows[0].Height - rows[1].Height) <= 1);
    }

    [Fact]
    public void RowsWithoutMinimumsAreEqual()
    {
        var rows = SceneLayoutEngine.DistributeRows(new PixelRect(0, 0, 900, 1000), [0, 0, 0, 0], 0);

        Assert.All(rows, r => Assert.Equal(250, r.Height));
    }
}
