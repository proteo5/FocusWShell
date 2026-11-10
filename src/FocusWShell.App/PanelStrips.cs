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
/// <param name="Display">Monitor auxiliar, o <c>null</c> en el principal.</param>
/// <param name="Band">Franja en píxeles físicos, encima de las ventanas del panel.</param>
public sealed record PanelStrip(string? Display, Home Home, string Title, PixelRect Band, int Dpi, IReadOnlyList<StripTab> Tabs)
{
    public string Key => $"{Display ?? "principal"}|{Home}";
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
        var principal = report.Layout.Principal;
        int top = principal.CenterColumn.Top;

        void Add(string? display, Home home, string title, PixelRect column, int dpi)
        {
            if (column.Top <= top || column.IsEmpty)
            {
                return; // Sin franja reservada (p. ej. Fill): no hay tira.
            }

            var tabs = TabsOf(workspace, kinds, display, home);
            if (tabs.Count > 0)
            {
                strips.Add(new PanelStrip(display, home, title, new PixelRect(column.Left, top, column.Right, column.Top), dpi, tabs));
            }
        }

        Add(null, Home.Context, "Contexto", principal.LeftColumn, principalDpi);
        Add(null, Home.Work, "Trabajo", principal.RightColumn, principalDpi);
        foreach (var aux in report.Layout.Aux)
        {
            int auxTop = aux.WorkArea.Top;
            foreach (var (name, column) in aux.Columns)
            {
                var home = name == "Contexto" ? Home.Context : Home.Work;
                if (aux.EffectiveTemplates.GetValueOrDefault(name) == PanelTemplate.Fill || column.Top <= auxTop)
                {
                    continue;
                }

                var tabs = TabsOf(workspace, kinds, aux.Display, home);
                if (tabs.Count > 0)
                {
                    strips.Add(new PanelStrip(aux.Display, home, name, new PixelRect(column.Left, auxTop, column.Right, column.Top),
                        auxDpi.GetValueOrDefault(aux.Display, 96), tabs));
                }
            }
        }

        return strips;
    }

    /// <summary>Pestañas de un panel, en el orden del modelo (el orden del panel).</summary>
    private static List<StripTab> TabsOf(Workspace workspace, Dictionary<nint, TargetKind> kinds, string? display, Home home) =>
        workspace.Windows
            .Where(w => w.Display == display && w.Home == home)
            .Select(w => new StripTab(w.Handle, w.Label, w switch
            {
                { Placement: Placement.Center } => TabState.InCenter,
                { Placement: Placement.Parked } => TabState.Parked,
                { Detached: true } => TabState.Detached,
                _ => kinds.GetValueOrDefault(w.Handle) == TargetKind.Minimized ? TabState.Hidden : TabState.Active,
            }))
            .ToList();
}
