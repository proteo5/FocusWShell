using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Tests.Layout;

/// <summary>Paneles cuyo borde exterior limita con otro monitor (sin desborde hacia fuera).</summary>
public class SharedEdgeTests
{
    // Monitor estándar a la derecha del ultrawide, medido el 2026-11-09.
    private static readonly PixelRect Standard = new(3440, 0, 5360, 1032);

    // Ultrawide, con el estándar a su derecha.
    private static readonly PixelRect Ultrawide = new(0, 0, 3440, 1392);

    private static LayoutWindow Win(int id, string label, int minW = 0) =>
        new(id, label, minW == 0 ? null : new PixelSize(minW, 0));

    private static SceneInput Scene(
        PixelRect area,
        IReadOnlyList<LayoutWindow> context,
        LayoutWindow? center,
        IReadOnlyList<LayoutWindow> work,
        bool leftShared,
        bool rightShared) =>
        new(
            area,
            new PanelInput("Contexto", PanelSide.Left, 18, PanelTemplate.Tiles, context, leftShared),
            55,
            center,
            new PanelInput("Trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, work, rightShared));

    private static WindowTarget Target(SceneLayoutResult r, int id) => r.Targets.Single(t => t.Handle == id);

    /// <summary>Ninguna ventana visible sale del área de trabajo por un borde compartido ni toca el Centro.</summary>
    private static void AssertStaysOnMonitor(SceneLayoutResult r, PixelRect area, bool leftShared, bool rightShared)
    {
        foreach (var t in r.Targets.Where(t => t.Kind == TargetKind.Visible))
        {
            if (leftShared)
            {
                Assert.True(t.Rect.Left >= area.Left, $"{t.Label} sale por la izquierda: {t.Rect}");
            }

            if (rightShared)
            {
                Assert.True(t.Rect.Right <= area.Right, $"{t.Label} sale por la derecha: {t.Rect}");
            }

            if (t.Region != "Centro")
            {
                Assert.True(t.Rect.Intersect(r.CenterColumn).IsEmpty, $"{t.Label} invade el Centro");
            }
        }
    }

    [Fact]
    public void SharedEdgeLetsCenterShrinkBelowHalf()
    {
        // Caso real: WhatsApp (486 px) en Contexto del monitor estándar, con el ultrawide a la izquierda.
        var r = SceneLayoutEngine.Compute(Scene(Standard, [Win(1, "WhatsApp", 486)], Win(2, "Explorer"), [Win(3, "ChatGPT")], true, false));

        Assert.Equal(new PixelRect(3440, 0, 3926, 1032), Target(r, 1).Rect);
        Assert.True(r.CenterColumn.Width < Standard.Width / 2);
        AssertStaysOnMonitor(r, Standard, true, false);
    }

    [Fact]
    public void FreeEdgeKeepsTheHalfFloorAndOverflowsOutward()
    {
        // El mismo caso sin monitor vecino: se mantiene la regla de M0.
        var r = SceneLayoutEngine.Compute(Scene(Standard, [Win(1, "WhatsApp", 486)], Win(2, "Explorer"), [Win(3, "ChatGPT")], false, false));

        Assert.Equal(960, r.CenterColumn.Width);
        Assert.True(Target(r, 1).Rect.Left < Standard.Left);
    }

    [Fact]
    public void CenterNeverGoesBelowItsOwnMinimum()
    {
        var r = SceneLayoutEngine.Compute(Scene(Standard, [Win(1, "WhatsApp", 486)], Win(2, "Editor", 1000), [Win(3, "ChatGPT")], true, false));

        Assert.True(r.CenterColumn.Width >= 1000);
        AssertStaysOnMonitor(r, Standard, true, false);
    }

    [Fact]
    public void WindowThatCannotFitLeavesTheLayout()
    {
        // GitHub Desktop (962) en Trabajo del ultrawide, cuyo borde derecho da al monitor estándar,
        // con un Centro que exige 2400 px: no hay sitio sin invadir el otro monitor.
        var r = SceneLayoutEngine.Compute(Scene(
            Ultrawide, [Win(1, "Slack", 670)], Win(2, "Editor", 2400), [Win(3, "GitHub", 962), Win(4, "Terminal")], false, true));

        Assert.Equal(TargetKind.Minimized, Target(r, 3).Kind);
        Assert.Contains(r.Notes, n => n.Contains("GitHub", StringComparison.Ordinal) && n.Contains("fuera del layout", StringComparison.Ordinal));

        // Terminal se queda con todo el panel.
        Assert.Equal(TargetKind.Visible, Target(r, 4).Kind);
        Assert.Equal(r.RightColumn, Target(r, 4).Rect);
        AssertStaysOnMonitor(r, Ultrawide, false, true);
    }

    [Fact]
    public void RemainingWindowsOfAStackKeepTheirOrder()
    {
        var area = Standard;
        var input = new SceneInput(
            area,
            new PanelInput("Contexto", PanelSide.Left, 18, PanelTemplate.Stack, [Win(1, "Docker", 1500), Win(2, "Chat")], true),
            55,
            Win(3, "Editor", 900),
            new PanelInput("Trabajo", PanelSide.Right, 27, PanelTemplate.Tiles, []));

        var r = SceneLayoutEngine.Compute(input);

        Assert.Equal(TargetKind.Minimized, Target(r, 1).Kind);
        Assert.Equal(TargetKind.Visible, Target(r, 2).Kind);
        AssertStaysOnMonitor(r, area, true, false);
    }

    [Fact]
    public void BothEdgesSharedNeverLeaveTheMonitor()
    {
        var r = SceneLayoutEngine.Compute(Scene(
            Standard, [Win(1, "Slack", 670)], Win(2, "Explorer"), [Win(3, "GitHub", 962)], true, true));

        AssertStaysOnMonitor(r, Standard, true, true);
        Assert.True(r.CenterColumn.Width >= (int)Math.Round(Standard.Width * 0.3));
    }
}
