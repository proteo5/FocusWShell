using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Paneles de un monitor auxiliar, de izquierda a derecha.</summary>
public sealed record AuxInput(string Display, PixelRect WorkArea, IReadOnlyList<PanelInput> Panels, int Gap = 0);

/// <summary>Resultado del motor de un monitor auxiliar.</summary>
/// <param name="Columns">Columna de cada panel visible, por nombre.</param>
public sealed record AuxLayoutResult(
    string Display,
    PixelRect WorkArea,
    IReadOnlyDictionary<string, PixelRect> Columns,
    IReadOnlyDictionary<string, PanelTemplate> EffectiveTemplates,
    IReadOnlyList<WindowTarget> Targets,
    IReadOnlyList<string> Notes);

/// <summary>
/// Layout de un monitor auxiliar (ADR 0008): sin Centro, sólo paneles en columnas.
/// <list type="number">
/// <item>Los paneles vacíos no ocupan sitio.</item>
/// <item>Si un panel está en <see cref="PanelTemplate.Fill"/>, su primera ventana se maximiza y todo lo
/// demás de ese monitor se minimiza. Si hay varios en Fill, gana el primero.</item>
/// <item>Si no, las columnas se reparten por peso; una columna que no llega al mínimo de su ventana
/// más ancha toma de las otras sin bajarlas del suyo.</item>
/// <item>En un auxiliar nada se desborda: la ventana que no cabe ni así sale del layout (se minimiza).</item>
/// </list>
/// </summary>
public static class AuxLayoutEngine
{
    public static AuxLayoutResult Compute(AuxInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var notes = new List<string>();
        var targets = new List<WindowTarget>();
        var area = input.WorkArea;
        int gap = input.Gap;
        var panels = input.Panels.Where(p => p.Windows.Count > 0).ToList();

        // 1. Fill: una ventana ocupa el monitor.
        if (panels.FirstOrDefault(p => p.Template == PanelTemplate.Fill) is { } fill)
        {
            var main = fill.Windows[0];
            targets.Add(new WindowTarget(main.Handle, main.Label, fill.Name, TargetKind.Maximized, area));
            foreach (var p in panels)
            {
                foreach (var w in p.Windows.Where(w => w.Handle != main.Handle))
                {
                    targets.Add(new WindowTarget(w.Handle, w.Label, p.Name, TargetKind.Minimized, default));
                }
            }

            var hidden = panels.Where(p => p != fill).Select(p => p.Name).ToList();
            if (hidden.Count > 0)
            {
                notes.Add($"{main.Label} ocupa el monitor ({fill.Name} en Fill); {string.Join(" y ", hidden)} se minimiza.");
            }

            return new AuxLayoutResult(
                input.Display,
                area,
                new Dictionary<string, PixelRect> { [fill.Name] = area },
                panels.ToDictionary(p => p.Name, p => p == fill ? PanelTemplate.Fill : PanelTemplate.Stack),
                targets,
                notes);
        }

        // 2. Template efectivo (tiles que no caben en alto pasan a pila).
        var resolved = panels.Select(p => SceneLayoutEngine.Resolve(p, area.Height, gap, notes)).ToList();

        // 3. Ventanas que no caben ni dejando a los demás paneles en su mínimo.
        int available = area.Width - (gap * Math.Max(0, resolved.Count - 1));
        for (int i = 0; i < resolved.Count; i++)
        {
            int othersNeed = resolved.Where((_, k) => k != i).Sum(r => r.RequiredWidth);
            int max = available - othersNeed;
            var tooWide = resolved[i].Panel.Windows.Where(w => (w.MinSize?.Width ?? 0) > max).ToList();
            if (tooWide.Count == 0)
            {
                continue;
            }

            foreach (var w in tooWide)
            {
                targets.Add(new WindowTarget(w.Handle, w.Label, resolved[i].Panel.Name, TargetKind.Minimized, default));
                notes.Add($"{w.Label} necesita {w.MinSize!.Value.Width} px de ancho y {resolved[i].Panel.Name} puede tener como mucho {max} en este monitor; se minimiza y queda fuera del layout.");
            }

            var remaining = resolved[i].Panel with { Windows = resolved[i].Panel.Windows.Except(tooWide).ToList() };
            resolved[i] = SceneLayoutEngine.Resolve(remaining, area.Height, gap, []);
        }

        resolved = resolved.Where(r => r.Visible.Count > 0).ToList();
        if (resolved.Count == 0)
        {
            return new AuxLayoutResult(input.Display, area, new Dictionary<string, PixelRect>(), new Dictionary<string, PanelTemplate>(), targets, notes);
        }

        // 4. Anchos: por peso y luego, a quien no llega a su mínimo, se le da a costa de los demás.
        var widths = Splitter.Columns(area, resolved.Select(r => r.Panel.Weight).ToList(), gap).Select(c => c.Width).ToArray();
        var ideal = widths.ToArray();
        for (int i = 0; i < resolved.Count; i++)
        {
            int missing = resolved[i].RequiredWidth - widths[i];
            for (int k = 0; k < resolved.Count && missing > 0; k++)
            {
                if (k == i)
                {
                    continue;
                }

                int spare = Math.Max(0, widths[k] - resolved[k].RequiredWidth);
                int take = Math.Min(spare, missing);
                widths[k] -= take;
                widths[i] += take;
                missing -= take;
            }

            if (widths[i] > ideal[i])
            {
                var widest = resolved[i].Visible.MaxBy(w => w.MinSize?.Width ?? 0)!;
                notes.Add($"{resolved[i].Panel.Name} se amplió a {widths[i]} px porque {widest.Label} necesita {resolved[i].RequiredWidth} px de ancho.");
            }
        }

        // 5. Columnas y filas.
        var columns = new Dictionary<string, PixelRect>();
        int left = area.Left;
        for (int i = 0; i < resolved.Count; i++)
        {
            int right = i == resolved.Count - 1 ? area.Right : left + widths[i];
            var column = new PixelRect(left, area.Top, right, area.Bottom);
            columns[resolved[i].Panel.Name] = column;
            var rows = SceneLayoutEngine.DistributeRows(column, resolved[i].Visible.Select(w => w.MinSize?.Height ?? 0).ToList(), gap);
            for (int r = 0; r < resolved[i].Visible.Count; r++)
            {
                var w = resolved[i].Visible[r];
                targets.Add(new WindowTarget(w.Handle, w.Label, resolved[i].Panel.Name, TargetKind.Visible, rows[r]));
            }

            foreach (var hidden in resolved[i].Panel.Windows.Skip(resolved[i].Visible.Count))
            {
                targets.Add(new WindowTarget(hidden.Handle, hidden.Label, resolved[i].Panel.Name, TargetKind.Minimized, default));
            }

            left = right + gap;
        }

        return new AuxLayoutResult(
            input.Display,
            area,
            columns,
            resolved.ToDictionary(r => r.Panel.Name, r => r.Template),
            targets,
            notes);
    }
}
