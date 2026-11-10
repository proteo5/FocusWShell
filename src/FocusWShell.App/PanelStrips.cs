using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Win32.Scenes;

namespace FocusWShell.App;

/// <summary>Estado de una ventana en la tira de pestañas de su panel.</summary>
public enum TabState
{
    /// <summary>Visible en el panel.</summary>
    Active,

    /// <summary>Minimizada por la pila (otra pestaña es la visible).</summary>
    Hidden,

    /// <summary>Estacionada.</summary>
    Parked,

    /// <summary>Está en el Centro y volverá aquí.</summary>
    InCenter,

    /// <summary>Fuera de layout: el usuario la movió.</summary>
    Detached,
}

public sealed record StripTab(nint Handle, string Label, TabState State);

/// <summary>Tira de pestañas de un panel: dónde va (franja reservada) y qué muestra.</summary>
/// <param name="PanelId">Panel al que pertenece (ADR 0011).</param>
/// <param name="Title">Nombre visible del panel.</param>
/// <param name="Band">Franja en píxeles físicos, encima de las ventanas del panel.</param>
public sealed record PanelStrip(string PanelId, string Title, PixelRect Band, int Dpi, IReadOnlyList<StripTab> Tabs)
{
    public string Key => PanelId;
}

/// <summary>Calcula las tiras a partir del modelo y del último plan aplicado.</summary>
public static class PanelStripBuilder
{
    public static IReadOnlyList<PanelStrip> Build(
        Workspace workspace,
        SceneApplyReport report,
        int principalDpi,
        IReadOnlyDictionary<string, int> auxDpi)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(auxDpi);
        var kinds = report.Applied.ToDictionary(a => a.Target.Handle, a => a.Target.Kind);
        var strips = new List<PanelStrip>();

        void Add(Panel? panel, PixelRect column, int top, int dpi, PanelTemplate effective)
        {
            // Sin franja reservada (p. ej. Fill) no hay tira.
            if (panel is null || column.IsEmpty || column.Top <= top || effective == PanelTemplate.Fill)
            {
                return;
            }

            var tabs = TabsOf(workspace, kinds, panel.Id);
            if (tabs.Count > 0)
            {
                strips.Add(new PanelStrip(panel.Id, panel.Name, new PixelRect(column.Left, top, column.Right, column.Top), dpi, tabs));
            }
        }

        var principal = report.Layout.Principal;
        int principalTop = principal.CenterColumn.Top;
        var composition = workspace.Composition;
        Add(composition.Left?.Panel, principal.LeftColumn, principalTop, principalDpi, PanelTemplate.Tiles);
        Add(composition.Right?.Panel, principal.RightColumn, principalTop, principalDpi, PanelTemplate.Tiles);
        foreach (var aux in report.Layout.Aux)
        {
            foreach (var (panelId, column) in aux.Columns)
            {
                Add(workspace.FindPanel(panelId), column, aux.WorkArea.Top, auxDpi.GetValueOrDefault(aux.Display, 96),
                    aux.EffectiveTemplates.GetValueOrDefault(panelId));
            }
        }

        return strips;
    }

    /// <summary>Pestañas de un panel, en el orden del modelo: las ventanas que se colocan en él.</summary>
    private static List<StripTab> TabsOf(Workspace workspace, Dictionary<nint, TargetKind> kinds, string panelId) =>
        workspace.Windows
            .Where(w => workspace.EffectivePanel(w).Id == panelId)
            .Select(w => new StripTab(w.Handle, w.Label, w switch
            {
                { Placement: Placement.Center } => TabState.InCenter,
                { Placement: Placement.Parked } => TabState.Parked,
                { Detached: true } => TabState.Detached,
                _ => kinds.GetValueOrDefault(w.Handle) == TargetKind.Minimized ? TabState.Hidden : TabState.Active,
            }))
            .ToList();
}
