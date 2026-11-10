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

        // 1. Template efectivo de cada panel según la altura disponible.
        var left = Resolve(input.Left, area.Height, gap, notes);
        var right = Resolve(input.Right, area.Height, gap, notes);

        // 2. Anchos ideales según los pesos.
        var ideal = Splitter.Columns(area, [input.Left.Weight, input.CenterWeight, input.Right.Weight], gap);
        int wl = ideal[0].Width, wc = ideal[1].Width, wr = ideal[2].Width;

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
        var excluded = new List<WindowTarget>();
        if (input.Left.OuterEdgeShared)
        {
            int maxLeft = wl + Math.Max(0, wc - leftFloor - Math.Max(0, needR - wr));
            left = ExcludeTooWide(left.Panel, maxLeft, area.Height, gap, excluded, notes);
            needL = left.RequiredWidth;
        }

        if (input.Right.OuterEdgeShared)
        {
            int maxRight = wr + Math.Max(0, wc - rightFloor - Math.Max(0, needL - wl));
            right = ExcludeTooWide(right.Panel, maxRight, area.Height, gap, excluded, notes);
            needR = right.RequiredWidth;
        }

        // 5. Cada panel crece a costa del Centro, sin bajarlo de su piso.
        wl = Grow(input.Left, left, wl, needL, ref wc, leftFloor, notes);
        wr = Grow(input.Right, right, wr, needR, ref wc, rightFloor, notes);

        // 6. Columnas definitivas (contiguas, con separación).
        var leftCol = new PixelRect(area.Left, area.Top, area.Left + wl, area.Bottom);
        var centerCol = new PixelRect(leftCol.Right + gap, area.Top, leftCol.Right + gap + wc, area.Bottom);
        var rightCol = new PixelRect(centerCol.Right + gap, area.Top, area.Right, area.Bottom);

        // 7. Destinos.
        var targets = new List<WindowTarget>();
        PlacePanel(left.Panel, left, leftCol, gap, targets, notes);
        if (input.Center is { } center)
        {
            targets.Add(new WindowTarget(center.Handle, center.Label, "Centro", TargetKind.Visible, FitCenter(center, centerCol, notes)));
        }

        PlacePanel(right.Panel, right, rightCol, gap, targets, notes);
        targets.AddRange(excluded);

        return new SceneLayoutResult(
            leftCol,
            centerCol,
            rightCol,
            new Dictionary<string, PanelTemplate> { [input.Left.Name] = left.Template, [input.Right.Name] = right.Template },
            targets,
            notes);
    }

    /// <param name="Panel">El panel con las ventanas que siguen en el layout.</param>
    private sealed record ResolvedPanel(PanelInput Panel, PanelTemplate Template, IReadOnlyList<LayoutWindow> Visible, int RequiredWidth);

    /// <summary>Saca del panel las ventanas más anchas que <paramref name="maxWidth"/> y vuelve a resolverlo.</summary>
    private static ResolvedPanel ExcludeTooWide(
        PanelInput panel, int maxWidth, int height, int gap, List<WindowTarget> excluded, List<string> notes)
    {
        var tooWide = panel.Windows.Where(w => (w.MinSize?.Width ?? 0) > maxWidth).ToList();
        foreach (var w in tooWide)
        {
            excluded.Add(new WindowTarget(w.Handle, w.Label, panel.Name, TargetKind.Minimized, default));
            notes.Add(
                $"{w.Label} necesita {w.MinSize!.Value.Width} px de ancho y {panel.Name} puede tener como mucho {maxWidth} " +
                "sin invadir el Centro ni el otro monitor; se minimiza y queda fuera del layout.");
        }

        // Al volver a resolver no se repiten las notas de template que ya se dieron.
        var remaining = panel with { Windows = panel.Windows.Except(tooWide).ToList() };
        return Resolve(remaining, height, gap, tooWide.Count > 0 ? notes : []);
    }

    private static ResolvedPanel Resolve(PanelInput panel, int height, int gap, List<string> notes)
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
                    $"{panel.Name}: sus {windows.Count} ventanas necesitan {needed} px de alto y hay {height}; " +
                    "se muestran como pila.");
            }
        }

        IReadOnlyList<LayoutWindow> visible = template == PanelTemplate.Stack ? [windows[0]] : windows;
        return new ResolvedPanel(panel, template, visible, visible.Max(w => w.MinSize?.Width ?? 0));
    }

    private static int Grow(
        PanelInput panel, ResolvedPanel resolved, int width, int needed, ref int centerWidth, int centerFloor, List<string> notes)
    {
        if (width >= needed)
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
        notes.Add($"{panel.Name} se amplió {take} px (el Centro queda en {centerWidth} px) porque {widest.Label} necesita {needed} px de ancho.");
        return width + take;
    }

    private static void PlacePanel(
        PanelInput panel, ResolvedPanel resolved, PixelRect column, int gap, List<WindowTarget> targets, List<string> notes)
    {
        if (resolved.Visible.Count == 0)
        {
            return;
        }

        var rows = DistributeRows(column, resolved.Visible.Select(w => w.MinSize?.Height ?? 0).ToList(), gap);
        for (int i = 0; i < resolved.Visible.Count; i++)
        {
            var w = resolved.Visible[i];
            var rect = AnchorOutward(w, rows[i], panel.Side, notes, panel.Name);
            targets.Add(new WindowTarget(w.Handle, w.Label, panel.Name, TargetKind.Visible, rect));
        }

        foreach (var hidden in panel.Windows.Skip(resolved.Visible.Count))
        {
            targets.Add(new WindowTarget(hidden.Handle, hidden.Label, panel.Name, TargetKind.Minimized, default));
        }
    }

    /// <summary>Si la ventana es más grande que su celda, el exceso va hacia el borde exterior (y hacia abajo).</summary>
    private static PixelRect AnchorOutward(LayoutWindow w, PixelRect cell, PanelSide side, List<string> notes, string panelName)
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

    private static PixelRect FitCenter(LayoutWindow center, PixelRect column, List<string> notes)
    {
        int minWidth = center.MinSize?.Width ?? 0;
        int minHeight = center.MinSize?.Height ?? 0;
        if (minWidth <= column.Width && minHeight <= column.Height)
        {
            return column;
        }

        // Caso extremo: ni quitando a los paneles cabe. Se centra; invade por igual ambos paneles.
        int width = Math.Max(column.Width, minWidth);
        int height = Math.Max(column.Height, minHeight);
        notes.Add($"{center.Label} no cabe en el Centro ({width}x{height} contra {column.Width}x{column.Height}); se centra sobre los paneles.");
        return PixelRect.FromSize(column.Left - ((width - column.Width) / 2), column.Top, width, height);
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
