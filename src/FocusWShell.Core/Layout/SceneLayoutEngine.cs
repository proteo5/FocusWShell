using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>
/// Calcula la geometría de una escena de tres columnas respetando los tamaños mínimos de las apps.
/// Reglas (docs/02-modelo-de-dominio.md#tamaños-mínimos):
/// <list type="number">
/// <item>Si la ventana del Centro no cabe, el Centro crece a costa de Contexto y luego de Trabajo,
/// sin bajar a ningún panel de lo que necesitan sus ventanas.</item>
/// <item>Un panel en tiles cuyas ventanas no caben en altura se muestra como pila.</item>
/// <item>Un panel cuya ventana visible no cabe en ancho crece a costa del Centro, sin bajar al Centro
/// de <see cref="SceneInput.MinCenterFraction"/> ni del mínimo de su ventana.</item>
/// <item>Si aun así no cabe, la ventana se ancla al borde exterior del monitor: el exceso queda fuera
/// de pantalla y nunca encima del Centro.</item>
/// <item>Una columna puede tener dos paneles, uno encima del otro: se reparten el alto y ninguno invade
/// al otro (la ventana que no cabe en su banda sale del layout).</item>
/// <item>Si ese borde exterior limita con otro monitor (<see cref="PanelInput.OuterEdgeShared"/>), el Centro
/// puede bajar hasta el mínimo de su ventana (piso <see cref="SceneInput.SharedEdgeMinCenterFraction"/>);
/// la ventana que ni así cabe sale del layout (se minimiza) en vez de invadir el otro monitor.</item>
/// </list>
/// </summary>
public static class SceneLayoutEngine
{
    public static SceneLayoutResult Compute(SceneInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var notes = new List<string>();
        var area = input.WorkArea;
        int gap = input.Gap;
        var excluded = new List<WindowTarget>();

        // Los paneles empiezan bajo su tira de pestañas; el Centro ocupa toda la altura.
        int inset = Math.Clamp(input.PanelTopInset, 0, area.Height / 2);

        // 1. Paneles de cada columna (hasta dos, uno encima del otro) y su template efectivo según el
        //    alto que les toca.
        var left = PanelColumn.Build(Column(input.Left, input.LeftLower), area.Top, area.Bottom, inset, gap, notes, excluded);
        var right = PanelColumn.Build(Column(input.Right, input.RightLower), area.Top, area.Bottom, inset, gap, notes, excluded);

        // 2. Anchos ideales según los pesos, en el orden de la vista.
        var focus = input.Focus;
        int wl, wc, wr;
        switch (focus)
        {
            case FocusPosition.Left:
            {
                var ideal = Splitter.Columns(area, [input.CenterWeight, input.Right.Weight, input.Left.Weight], gap);
                (wc, wr, wl) = (ideal[0].Width, ideal[1].Width, ideal[2].Width);
                break;
            }

            case FocusPosition.Right:
            {
                var ideal = Splitter.Columns(area, [input.Left.Weight, input.Right.Weight, input.CenterWeight], gap);
                (wl, wr, wc) = (ideal[0].Width, ideal[1].Width, ideal[2].Width);
                break;
            }

            default:
            {
                var ideal = Splitter.Columns(area, [input.Left.Weight, input.CenterWeight, input.Right.Weight], gap);
                (wl, wc, wr) = (ideal[0].Width, ideal[1].Width, ideal[2].Width);
                break;
            }
        }

        int needL = left.RequiredWidth;
        int needR = right.RequiredWidth;
        int needC = input.Center?.MinSize?.Width ?? 0;
        int centerFloor = Math.Max(needC, Math.Min(wc, (int)Math.Round(area.Width * input.MinCenterFraction)));
        int sharedFloor = Math.Max(needC, Math.Min(wc, (int)Math.Round(area.Width * input.SharedEdgeMinCenterFraction)));

        // 3. El Centro crece primero a costa de Contexto (izquierda) y luego de Trabajo (derecha).
        if (wc < needC)
        {
            int missing = needC - wc;
            int fromLeft = Math.Clamp(wl - needL, 0, missing);
            wl -= fromLeft;
            missing -= fromLeft;
            int fromRight = Math.Clamp(wr - needR, 0, missing);
            wr -= fromRight;
            missing -= fromRight;
            wc = needC - missing;
            notes.Add($"El Centro se amplió a {wc} px para que quepa {input.Center!.Label} (mínimo {needC} px).");
        }

        // 4. Un panel cuyo borde exterior da a otro monitor no puede desbordar: saca del layout las
        //    ventanas que no caben ni con el Centro en su piso compartido (reservando lo que necesita
        //    el otro panel).
        int leftFloor = input.Left.OuterEdgeShared ? sharedFloor : centerFloor;
        int rightFloor = input.Right.OuterEdgeShared ? sharedFloor : centerFloor;
        if (input.Left.OuterEdgeShared)
        {
            int maxLeft = wl + Math.Max(0, wc - leftFloor - Math.Max(0, needR - wr));
            left.ExcludeTooWide(maxLeft, excluded, notes, SharedEdgeNote);
            needL = left.RequiredWidth;
        }

        if (input.Right.OuterEdgeShared)
        {
            int maxRight = wr + Math.Max(0, wc - rightFloor - Math.Max(0, needL - wl));
            right.ExcludeTooWide(maxRight, excluded, notes, SharedEdgeNote);
            needR = right.RequiredWidth;
        }

        // 5. Cada columna crece a costa del Centro, sin bajarlo de su piso.
        wl = Grow(left, wl, needL, ref wc, leftFloor, notes);
        wr = Grow(right, wr, needR, ref wc, rightFloor, notes);

        // 6. Con el foco en un extremo, el panel de la derecha (Trabajo) queda entre el foco y el otro
        //    panel: no puede desbordar hacia ningún lado, así que la ventana que no cabe sale del layout.
        if (focus != FocusPosition.Center)
        {
            right.ExcludeTooWide(wr, excluded, notes, (w, panel, limit) =>
                $"{w.Label} necesita {w.MinSize!.Value.Width} px de ancho y {panel.DisplayTitle} tiene {limit} entre el foco y el otro panel; " +
                "se minimiza y queda fuera del layout.");
        }

        // 7. Columnas definitivas (contiguas, con separación), en el orden de la vista. La última se queda
        //    con lo que sobre por redondeo.
        int x0 = area.Left;
        PixelRect Next(int width, int top, bool last)
        {
            var rect = new PixelRect(x0, top, last ? area.Right : x0 + width, area.Bottom);
            x0 = rect.Right + gap;
            return rect;
        }

        PixelRect leftCol, centerCol, rightCol;
        PanelSide? leftEdge, rightEdge;
        switch (focus)
        {
            case FocusPosition.Left:
                centerCol = Next(wc, area.Top, false);
                rightCol = Next(wr, area.Top + inset, false);
                leftCol = Next(wl, area.Top + inset, true);
                (leftEdge, rightEdge) = (PanelSide.Right, null);
                break;
            case FocusPosition.Right:
                leftCol = Next(wl, area.Top + inset, false);
                rightCol = Next(wr, area.Top + inset, false);
                centerCol = Next(wc, area.Top, true);
                (leftEdge, rightEdge) = (PanelSide.Left, null);
                break;
            default:
                leftCol = Next(wl, area.Top + inset, false);
                centerCol = Next(wc, area.Top, false);
                rightCol = Next(wr, area.Top + inset, true);
                (leftEdge, rightEdge) = (PanelSide.Left, PanelSide.Right);
                break;
        }

        // 8. Destinos.
        var targets = new List<WindowTarget>();
        var areas = new Dictionary<string, PixelRect>();
        var bands = new Dictionary<string, PixelRect>();
        left.Place(leftCol.Left, leftCol.Right, leftEdge, gap, targets, notes, areas, bands);
        if (input.Center is { } center)
        {
            targets.Add(new WindowTarget(center.Handle, center.Label, "Centro", TargetKind.Visible, FitCenter(center, centerCol, focus, notes)));
        }

        right.Place(rightCol.Left, rightCol.Right, rightEdge, gap, targets, notes, areas, bands);
        targets.AddRange(excluded);

        var templates = new Dictionary<string, PanelTemplate>();
        foreach (var p in Column(input.Left, input.LeftLower).Concat(Column(input.Right, input.RightLower)))
        {
            templates[p.Name] = p.Template;
        }

        foreach (var (name, template) in left.Templates.Concat(right.Templates))
        {
            templates[name] = template;
        }

        return new SceneLayoutResult(leftCol, centerCol, rightCol, templates, targets, notes)
        {
            Bands = bands,
            StripHeight = inset,
        };
    }

    private static IReadOnlyList<PanelInput> Column(PanelInput first, PanelInput? second) =>
        second is null ? [first] : [first, second];

    private static string SharedEdgeNote(LayoutWindow w, PanelInput panel, int maxWidth) =>
        $"{w.Label} necesita {w.MinSize!.Value.Width} px de ancho y {panel.Name} puede tener como mucho {maxWidth} " +
        "sin invadir el Centro ni el otro monitor; se minimiza y queda fuera del layout.";

    /// <param name="Panel">El panel con las ventanas que siguen en el layout.</param>
    internal sealed record ResolvedPanel(PanelInput Panel, PanelTemplate Template, IReadOnlyList<LayoutWindow> Visible, int RequiredWidth);

    internal static ResolvedPanel Resolve(PanelInput panel, int height, int gap, List<string> notes)
    {
        var windows = panel.Windows;
        if (windows.Count == 0)
        {
            return new ResolvedPanel(panel, panel.Template, [], 0);
        }

        var template = panel.Template;
        if (template == PanelTemplate.Tiles && windows.Count > 1)
        {
            int needed = windows.Sum(w => w.MinSize?.Height ?? 0) + (gap * (windows.Count - 1));
            if (needed > height)
            {
                template = PanelTemplate.Stack;
                notes.Add(
                    $"{panel.DisplayTitle}: sus {windows.Count} ventanas necesitan {needed} px de alto y hay {height}; " +
                    "se muestran como pila.");
            }
        }

        // Fill sólo tiene sentido en monitores auxiliares; aquí se comporta como una pila.
        IReadOnlyList<LayoutWindow> visible = template is PanelTemplate.Stack or PanelTemplate.Fill ? [windows[0]] : windows;
        return new ResolvedPanel(panel, template, visible, visible.Max(w => w.MinSize?.Width ?? 0));
    }

    private static int Grow(PanelColumn column, int width, int needed, ref int centerWidth, int centerFloor, List<string> notes)
    {
        if (width >= needed || column.Widest is not { } resolved)
        {
            return width;
        }

        int take = Math.Clamp(needed - width, 0, Math.Max(0, centerWidth - centerFloor));
        if (take == 0)
        {
            return width;
        }

        centerWidth -= take;
        var widest = resolved.Visible.MaxBy(w => w.MinSize?.Width ?? 0)!;
        notes.Add($"{resolved.Panel.DisplayTitle} se amplió {take} px (el Centro queda en {centerWidth} px) porque {widest.Label} necesita {needed} px de ancho.");
        return width + take;
    }

    /// <summary>Si la ventana es más grande que su celda, el exceso va hacia el borde exterior (y hacia abajo).</summary>
    internal static PixelRect AnchorOutward(LayoutWindow w, PixelRect cell, PanelSide side, List<string> notes, string panelName)
    {
        int width = Math.Max(cell.Width, w.MinSize?.Width ?? 0);
        int height = Math.Max(cell.Height, w.MinSize?.Height ?? 0);
        if (width > cell.Width)
        {
            notes.Add(
                $"{w.Label} necesita {width} px de ancho y {panelName} tiene {cell.Width}; " +
                $"se ancla al borde {(side == PanelSide.Left ? "izquierdo" : "derecho")} y {width - cell.Width} px quedan fuera de pantalla.");
        }

        if (height > cell.Height)
        {
            notes.Add($"{w.Label} necesita {height} px de alto y su celda tiene {cell.Height}; {height - cell.Height} px quedan por debajo.");
        }

        int left = side == PanelSide.Left ? cell.Right - width : cell.Left;
        return PixelRect.FromSize(left, cell.Top, width, height);
    }

    private static PixelRect FitCenter(LayoutWindow center, PixelRect column, FocusPosition focus, List<string> notes)
    {
        int minWidth = center.MinSize?.Width ?? 0;
        int minHeight = center.MinSize?.Height ?? 0;
        if (minWidth <= column.Width && minHeight <= column.Height)
        {
            return column;
        }

        // Caso extremo: ni quitando a los paneles cabe. En medio, se centra e invade por igual ambos
        // paneles; en un extremo, se ancla a su borde del monitor e invade el panel de al lado.
        int width = Math.Max(column.Width, minWidth);
        int height = Math.Max(column.Height, minHeight);
        int left = focus switch
        {
            FocusPosition.Left => column.Left,
            FocusPosition.Right => column.Right - width,
            _ => column.Left - ((width - column.Width) / 2),
        };
        notes.Add(focus == FocusPosition.Center
            ? $"{center.Label} no cabe en el Centro ({width}x{height} contra {column.Width}x{column.Height}); se centra sobre los paneles."
            : $"{center.Label} no cabe en el foco ({width}x{height} contra {column.Width}x{column.Height}); se ancla al borde del monitor y tapa parte del panel de al lado.");
        return PixelRect.FromSize(left, column.Top, width, height);
    }

    /// <summary>
    /// Reparte la altura en filas iguales, pero ninguna por debajo de su mínimo: las filas con mínimo
    /// mayor que la parte igual se fijan en su mínimo y el resto se reparte entre las demás.
    /// </summary>
    internal static IReadOnlyList<PixelRect> DistributeRows(PixelRect area, IReadOnlyList<int> minHeights, int gap)
    {
        int n = minHeights.Count;
        int available = area.Height - (gap * (n - 1));
        var heights = new int?[n];
        bool changed = true;
        while (changed)
        {
            changed = false;
            int fixedTotal = heights.Where(h => h.HasValue).Sum(h => h!.Value);
            int freeCount = heights.Count(h => !h.HasValue);
            if (freeCount == 0)
            {
                break;
            }

            double share = (double)(available - fixedTotal) / freeCount;
            for (int i = 0; i < n; i++)
            {
                if (!heights[i].HasValue && minHeights[i] > share)
                {
                    heights[i] = minHeights[i];
                    changed = true;
                }
            }
        }

        // Reparto entero de lo que queda entre las filas libres, sin perder píxeles.
        var free = Enumerable.Range(0, n).Where(i => !heights[i].HasValue).ToList();
        int remaining = available - heights.Where(h => h.HasValue).Sum(h => h!.Value);
        for (int k = 0; k < free.Count; k++)
        {
            int share = (remaining / (free.Count - k));
            heights[free[k]] = share;
            remaining -= share;
        }

        var rows = new List<PixelRect>(n);
        int top = area.Top;
        foreach (var h in heights)
        {
            rows.Add(new PixelRect(area.Left, top, area.Right, top + h!.Value));
            top += h!.Value + gap;
        }

        return rows;
    }
}
