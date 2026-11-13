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

    /// <summary>Minimizada en su panel.</summary>
    Parked,

    /// <summary>Está en el Centro y volverá aquí.</summary>
    InCenter,

    /// <summary>Fuera de layout: el usuario la movió.</summary>
    Detached,
}

public sealed record StripTab(nint Handle, string Label, TabState State)
{
    /// <summary>Tamaño de tile de la ventana (ADR 0015).</summary>
    public TileSize Size { get; init; } = TileSize.Small;
}

/// <summary>Tira de pestañas de un panel: dónde va (franja reservada) y qué muestra.</summary>
/// <param name="PanelId">Panel al que pertenece (ADR 0011).</param>
/// <param name="Title">Nombre visible del panel.</param>
/// <param name="Band">Franja en píxeles físicos, encima de las ventanas del panel.</param>
public sealed record PanelStrip(string PanelId, string Title, PixelRect Band, int Dpi, IReadOnlyList<StripTab> Tabs)
{
    /// <summary>Filas del panel en tiles si no caben todas (▲ ▼).</summary>
    public TilePages? Pages { get; init; }

    /// <summary>Presentación efectiva del panel: el tamaño de tile sólo tiene sentido en tiles.</summary>
    public PanelTemplate Template { get; init; } = PanelTemplate.Tiles;

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

        // Cada panel con ventanas tiene su banda; la tira ocupa la franja de arriba de la banda.
        void Add(string panelId, PixelRect band, int stripHeight, int dpi, PanelTemplate effective, TilePages? pages)
        {
            // Sin franja reservada (p. ej. Fill) no hay tira.
            if (workspace.FindPanel(panelId) is not { } panel || band.IsEmpty || stripHeight <= 0 || effective == PanelTemplate.Fill)
            {
                return;
            }

            var tabs = TabsOf(workspace, kinds, panel.Id);
            if (tabs.Count > 0)
            {
                strips.Add(new PanelStrip(panel.Id, panel.Name, band with { Bottom = band.Top + stripHeight }, dpi, tabs)
                {
                    Pages = pages,
                    Template = effective,
                });
            }
        }

        // En el monitor de la pantalla completa no se muestran tiras: la ventana lo ocupa entero.
        var fullscreen = workspace.Composition.Fullscreen;
        var principal = report.Layout.Principal;
        IEnumerable<KeyValuePair<string, PixelRect>> principalBands = fullscreen is { Display: null } ? [] : principal.Bands;
        foreach (var (panelId, band) in principalBands)
        {
            Add(panelId, band, principal.StripHeight, principalDpi, principal.EffectiveTemplates.GetValueOrDefault(panelId), principal.Pages.GetValueOrDefault(panelId));
        }

        foreach (var aux in report.Layout.Aux.Where(a => fullscreen?.Display is not { } d || !string.Equals(d, a.Display, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var (panelId, band) in aux.Bands)
            {
                Add(panelId, band, aux.StripHeight, auxDpi.GetValueOrDefault(aux.Display, 96), aux.EffectiveTemplates.GetValueOrDefault(panelId), aux.Pages.GetValueOrDefault(panelId));
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
                { Placement: Placement.Center or Placement.Fullscreen } => TabState.InCenter,
                { Placement: Placement.Parked } => TabState.Parked,
                { Detached: true } => TabState.Detached,
                _ => kinds.GetValueOrDefault(w.Handle) == TargetKind.Minimized ? TabState.Hidden : TabState.Active,
            }) { Size = w.Size })
            .ToList();
}
