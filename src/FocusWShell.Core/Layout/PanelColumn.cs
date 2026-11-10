using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>
/// Una columna de paneles (ADR 0011): uno, o dos uno encima del otro. Los paneles sin ventanas no ocupan
/// sitio; con dos, se reparten el alto a partes iguales y cada uno lleva su franja de pestañas arriba.
/// Ningún panel invade al otro: la ventana que no cabe en el alto de su banda sale del layout.
/// </summary>
internal sealed class PanelColumn
{
    private readonly List<Band> _bands;
    private readonly int _inset;

    /// <summary>Un panel de la columna, ya resuelto, y su banda vertical (incluida su franja).</summary>
    private sealed record Band(SceneLayoutEngine.ResolvedPanel Resolved, int Top, int Bottom);

    private PanelColumn(List<Band> bands, int inset, double weight)
    {
        _bands = bands;
        _inset = inset;
        Weight = weight;
    }

    /// <summary>Peso de la columna: el de su primer panel.</summary>
    public double Weight { get; }

    /// <summary>Lo que necesita la columna de ancho: la ventana visible más ancha de sus paneles.</summary>
    public int RequiredWidth => _bands.Count == 0 ? 0 : _bands.Max(b => b.Resolved.RequiredWidth);

    /// <summary>El panel que fija <see cref="RequiredWidth"/>, para las notas.</summary>
    public SceneLayoutEngine.ResolvedPanel? Widest => _bands.Count == 0 ? null : _bands.MaxBy(b => b.Resolved.RequiredWidth)!.Resolved;

    public bool HasVisible => _bands.Any(b => b.Resolved.Visible.Count > 0);

    /// <summary>Template efectivo de cada panel de la columna con ventanas.</summary>
    public IEnumerable<KeyValuePair<string, PanelTemplate>> Templates =>
        _bands.Select(b => KeyValuePair.Create(b.Resolved.Panel.Name, b.Resolved.Template));

    /// <param name="panels">Paneles de la columna; se ordenan por <see cref="PanelInput.Slot"/>.</param>
    /// <param name="excluded">Donde van las ventanas que salen del layout por no caber en alto.</param>
    public static PanelColumn Build(
        IReadOnlyList<PanelInput> panels, int top, int bottom, int inset, int gap, List<string> notes, List<WindowTarget> excluded)
    {
        double weight = panels.Count > 0 ? panels[0].Weight : 1;
        var active = panels.Where(p => p.Windows.Count > 0).OrderBy(p => p.Slot).ToList();
        var bands = new List<Band>();
        if (active.Count == 0)
        {
            return new PanelColumn(bands, inset, weight);
        }

        // Cada panel, su parte del alto; nunca menos que su franja más un mínimo para sus ventanas.
        var spans = Splitter.Rows(new PixelRect(0, top, 1, bottom), active.Select(p => p.Height > 0 ? p.Height : 1).ToList(), gap);
        if (spans.Count == 2)
        {
            // Lo que necesita cada banda: su franja y la ventana más alta (en pestañas, se ve una sola).
            // Si caben las dos, el reparto pedido se lleva al límite en vez de sacar ventanas del layout.
            int Need(PanelInput p) => inset + Math.Max(StackSeparator.MinWindowHeight, p.Windows.Max(w => w.MinSize?.Height ?? 0));
            int need0 = Need(active[0]), need1 = Need(active[1]);
            if (need0 + need1 <= bottom - top - gap)
            {
                int split = Math.Clamp(spans[0].Bottom, top + need0, bottom - gap - need1);
                if (split != spans[0].Bottom)
                {
                    var (short_, needed) = split > spans[0].Bottom ? (active[0], need0) : (active[1], need1);
                    notes.Add($"{short_.DisplayTitle} necesita {needed} px de alto en su columna; el reparto con el otro panel se lleva a ese límite.");
                    spans = [new PixelRect(0, top, 1, split), new PixelRect(0, split + gap, 1, bottom)];
                }
            }
        }

        for (int i = 0; i < active.Count; i++)
        {
            var panel = active[i];
            int height = spans[i].Height - inset;
            if (active.Count > 1)
            {
                // Compartiendo columna, desbordar hacia abajo taparía al otro panel.
                var tooTall = panel.Windows.Where(w => (w.MinSize?.Height ?? 0) > height).ToList();
                foreach (var w in tooTall)
                {
                    excluded.Add(new WindowTarget(w.Handle, w.Label, panel.Name, TargetKind.Minimized, default));
                    notes.Add(
                        $"{w.Label} necesita {w.MinSize!.Value.Height} px de alto y {panel.DisplayTitle} tiene {height} " +
                        "porque comparte la columna; se minimiza y queda fuera del layout.");
                }

                panel = panel with { Windows = panel.Windows.Except(tooTall).ToList() };
            }

            bands.Add(new Band(SceneLayoutEngine.Resolve(panel, height, gap, notes), spans[i].Top, spans[i].Bottom));
        }

        return new PanelColumn(bands, inset, weight);
    }

    /// <summary>Saca del layout las ventanas más anchas que <paramref name="maxWidth"/> y vuelve a resolver sus paneles.</summary>
    public void ExcludeTooWide(
        int maxWidth, List<WindowTarget> excluded, List<string> notes, Func<LayoutWindow, PanelInput, int, string> note)
    {
        for (int i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i];
            var panel = band.Resolved.Panel;
            var tooWide = panel.Windows.Where(w => (w.MinSize?.Width ?? 0) > maxWidth).ToList();
            if (tooWide.Count == 0)
            {
                continue;
            }

            foreach (var w in tooWide)
            {
                excluded.Add(new WindowTarget(w.Handle, w.Label, panel.Name, TargetKind.Minimized, default));
                notes.Add(note(w, panel, maxWidth));
            }

            // Al volver a resolver no se repiten las notas de template que ya se dieron.
            var remaining = panel with { Windows = panel.Windows.Except(tooWide).ToList() };
            _bands[i] = band with { Resolved = SceneLayoutEngine.Resolve(remaining, band.Bottom - band.Top - _inset, 0, []) };
        }
    }

    /// <summary>
    /// Coloca las ventanas de cada panel en su banda, entre <paramref name="left"/> y <paramref name="right"/>.
    /// </summary>
    /// <param name="outerEdge">
    /// Borde del monitor que toca la columna, si toca alguno: una ventana más ancha que su celda se ancla a
    /// él. <c>null</c> (auxiliares, panel de en medio): ninguna desborda.
    /// </param>
    /// <param name="areas">Área de ventanas de cada panel (bajo su franja), por nombre.</param>
    /// <param name="bands">Banda de cada panel, incluida su franja, por nombre.</param>
    public void Place(
        int left,
        int right,
        PanelSide? outerEdge,
        int gap,
        List<WindowTarget> targets,
        List<string> notes,
        Dictionary<string, PixelRect> areas,
        Dictionary<string, PixelRect> bands)
    {
        foreach (var band in _bands)
        {
            var resolved = band.Resolved;
            var panel = resolved.Panel;
            if (resolved.Visible.Count > 0)
            {
                var windowArea = new PixelRect(left, band.Top + _inset, right, band.Bottom);
                areas[panel.Name] = windowArea;
                bands[panel.Name] = new PixelRect(left, band.Top, right, band.Bottom);
                var rows = SceneLayoutEngine.DistributeRows(windowArea, resolved.Visible.Select(w => w.MinSize?.Height ?? 0).ToList(), gap);
                for (int i = 0; i < resolved.Visible.Count; i++)
                {
                    var w = resolved.Visible[i];
                    var rect = outerEdge is { } edge ? SceneLayoutEngine.AnchorOutward(w, rows[i], edge, notes, panel.DisplayTitle) : rows[i];
                    targets.Add(new WindowTarget(w.Handle, w.Label, panel.Name, TargetKind.Visible, rect));
                }
            }

            foreach (var hidden in panel.Windows.Skip(resolved.Visible.Count))
            {
                targets.Add(new WindowTarget(hidden.Handle, hidden.Label, panel.Name, TargetKind.Minimized, default));
            }
        }
    }
}
