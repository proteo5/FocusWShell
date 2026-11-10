using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Paneles de un monitor auxiliar, de izquierda a derecha.</summary>
/// <param name="TopInset">Franja reservada arriba de cada panel para su tira de pestañas (no en Fill).</param>
public sealed record AuxInput(string Display, PixelRect WorkArea, IReadOnlyList<PanelInput> Panels, int Gap = 0, int TopInset = 0)
{
    /// <summary>Ancho mínimo de una columna de tiles; 0 = una sola columna.</summary>
    public int MinCellWidth { get; init; }

    /// <summary>Alto mínimo de una fila de tiles.</summary>
    public int MinRowHeight { get; init; }

    /// <summary>
    /// No maximizar (Fill): el monitor comparte pantalla con el deck fijado (ADR 0013, fase 4), así que la
    /// ventana de un panel en Fill ocupa el área que queda, bajo su tira de pestañas, en vez de maximizarse.
    /// </summary>
    public bool NoMaximize { get; init; }
}

/// <summary>Resultado del motor de un monitor auxiliar.</summary>
/// <param name="Columns">Columna de cada panel visible, por nombre.</param>
public sealed record AuxLayoutResult(
    string Display,
    PixelRect WorkArea,
    IReadOnlyDictionary<string, PixelRect> Columns,
    IReadOnlyDictionary<string, PanelTemplate> EffectiveTemplates,
    IReadOnlyList<WindowTarget> Targets,
    IReadOnlyList<string> Notes)
{
    /// <summary>Nombre visible de cada panel, por identificador.</summary>
    public IReadOnlyDictionary<string, string> Titles { get; init; } = new Dictionary<string, string>();

    /// <summary>Banda de cada panel con ventanas, incluida su franja de pestañas (no en Fill).</summary>
    public IReadOnlyDictionary<string, PixelRect> Bands { get; init; } = new Dictionary<string, PixelRect>();

    /// <summary>Alto de la franja de pestañas de cada banda.</summary>
    public int StripHeight { get; init; }

    /// <summary>Filas de los paneles en tiles que no caben enteros, por nombre.</summary>
    public IReadOnlyDictionary<string, TilePages> Pages { get; init; } = new Dictionary<string, TilePages>();

    /// <summary>Área (bajo su franja) de cada panel en lista: ahí va la ventana de la lista.</summary>
    public IReadOnlyDictionary<string, PixelRect> Lists { get; init; } = new Dictionary<string, PixelRect>();
}

/// <summary>
/// Layout de un monitor auxiliar (ADR 0008): sin Centro, sólo paneles en columnas.
/// <list type="number">
/// <item>Los paneles vacíos no ocupan sitio.</item>
/// <item>Si un panel está en <see cref="PanelTemplate.Fill"/>, su primera ventana se maximiza y todo lo
/// demás de ese monitor se minimiza. Si hay varios en Fill, gana el primero.</item>
/// <item>Si no, cada lado es una columna, con hasta dos paneles uno encima del otro que se reparten el
/// alto (ninguno invade al otro). Las columnas se reparten por peso; una columna que no llega al mínimo de su ventana
/// más ancha toma de las otras sin bajarlas del suyo.</item>
/// <item>En un auxiliar nada se desborda: la ventana que no cabe ni así sale del layout (se minimiza).</item>
/// </list>
/// </summary>
public static class AuxLayoutEngine
{
    public static AuxLayoutResult Compute(AuxInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ComputeCore(input) with { Titles = input.Panels.ToDictionary(p => p.Name, p => p.DisplayTitle) };
    }

    private static AuxLayoutResult ComputeCore(AuxInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var notes = new List<string>();
        var targets = new List<WindowTarget>();
        var area = input.WorkArea;
        int gap = input.Gap;
        var panels = input.Panels.Where(p => p.Windows.Count > 0 || p.Template == PanelTemplate.List).ToList();

        // 1. Fill: una ventana ocupa el monitor.
        if (panels.FirstOrDefault(p => p.Template == PanelTemplate.Fill) is { } fill)
        {
            var main = fill.Windows[0];
            int fillInset = input.NoMaximize ? Math.Clamp(input.TopInset, 0, area.Height / 2) : 0;
            var fillArea = area with { Top = area.Top + fillInset };
            targets.Add(input.NoMaximize
                ? new WindowTarget(main.Handle, main.Label, fill.Name, TargetKind.Visible, fillArea)
                : new WindowTarget(main.Handle, main.Label, fill.Name, TargetKind.Maximized, area));
            foreach (var p in panels)
            {
                foreach (var w in p.Windows.Where(w => w.Handle != main.Handle))
                {
                    targets.Add(new WindowTarget(w.Handle, w.Label, p.Name, TargetKind.Minimized, default));
                }
            }

            var hidden = panels.Where(p => p != fill).Select(p => p.DisplayTitle).ToList();
            if (hidden.Count > 0)
            {
                notes.Add($"{main.Label} ocupa el monitor ({fill.DisplayTitle} en Fill); {string.Join(" y ", hidden)} se minimiza.");
            }

            // Sin maximizar, el panel en Fill lleva su tira de pestañas (como una pila que ocupa el monitor).
            return new AuxLayoutResult(
                input.Display,
                area,
                new Dictionary<string, PixelRect> { [fill.Name] = fillArea },
                panels.ToDictionary(p => p.Name, p => p == fill && !input.NoMaximize ? PanelTemplate.Fill : PanelTemplate.Stack),
                targets,
                notes)
            {
                Bands = input.NoMaximize ? new Dictionary<string, PixelRect> { [fill.Name] = area } : new Dictionary<string, PixelRect>(),
                StripHeight = fillInset,
            };
        }

        // 2. Columnas por lado (de izquierda a derecha); en cada una, hasta dos paneles apilados con su
        //    template efectivo según el alto que les toca (tiles que no caben pasan a pila).
        int inset = Math.Clamp(input.TopInset, 0, area.Height / 2);
        var cols = panels
            .GroupBy(p => p.Side)
            .Select(g => PanelColumn.Build([.. g], area.Top, area.Bottom, inset, gap, notes, targets))
            .ToList();

        // 3. Ventanas que no caben ni dejando a las demás columnas en su mínimo.
        int available = area.Width - (gap * Math.Max(0, cols.Count - 1));
        for (int i = 0; i < cols.Count; i++)
        {
            int othersNeed = cols.Where((_, k) => k != i).Sum(c => c.RequiredWidth);
            int max = available - othersNeed;
            cols[i].ExcludeTooWide(max, targets, notes, (w, panel, limit) =>
                $"{w.Label} necesita {w.MinSize!.Value.Width} px de ancho y {panel.DisplayTitle} puede tener como mucho {limit} en este monitor; se minimiza y queda fuera del layout.");
        }

        cols = cols.Where(c => c.HasVisible).ToList();
        if (cols.Count == 0)
        {
            return new AuxLayoutResult(input.Display, area, new Dictionary<string, PixelRect>(), new Dictionary<string, PanelTemplate>(), targets, notes);
        }

        // 4. Anchos: por peso y luego, a quien no llega a su mínimo, se le da a costa de los demás.
        var widths = Splitter.Columns(area, cols.Select(c => c.Weight).ToList(), gap).Select(c => c.Width).ToArray();
        var ideal = widths.ToArray();
        for (int i = 0; i < cols.Count; i++)
        {
            int missing = cols[i].RequiredWidth - widths[i];
            for (int k = 0; k < cols.Count && missing > 0; k++)
            {
                if (k == i)
                {
                    continue;
                }

                int spare = Math.Max(0, widths[k] - cols[k].RequiredWidth);
                int take = Math.Min(spare, missing);
                widths[k] -= take;
                widths[i] += take;
                missing -= take;
            }

            if (widths[i] > ideal[i] && cols[i].Widest is { } widestPanel)
            {
                var widest = widestPanel.Visible.MaxBy(w => w.MinSize?.Width ?? 0)!;
                notes.Add($"{widestPanel.Panel.DisplayTitle} se amplió a {widths[i]} px porque {widest.Label} necesita {cols[i].RequiredWidth} px de ancho.");
            }
        }

        // 5. Columnas, bandas y filas.
        var columns = new Dictionary<string, PixelRect>();
        var bands = new Dictionary<string, PixelRect>();
        var pages = new Dictionary<string, TilePages>();
        var grid = new GridOptions(input.MinCellWidth, input.MinRowHeight) { Lists = [] };
        int left = area.Left;
        for (int i = 0; i < cols.Count; i++)
        {
            int right = i == cols.Count - 1 ? area.Right : left + widths[i];
            cols[i].Place(left, right, outerEdge: null, gap, grid, targets, notes, columns, bands, pages);
            left = right + gap;
        }

        return new AuxLayoutResult(
            input.Display,
            area,
            columns,
            cols.SelectMany(c => c.Templates).ToDictionary(t => t.Key, t => t.Value),
            targets,
            notes)
        {
            Bands = bands,
            StripHeight = inset,
            Pages = pages,
            Lists = grid.Lists,
        };
    }
}
