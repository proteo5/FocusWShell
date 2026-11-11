using FocusWShell.Core.Layout;

namespace FocusWShell.Core.Domain;

/// <summary>Dónde va una ventana dentro de una escena guardada.</summary>
public enum ScenePlacement
{
    InPanel,
    Center,
    Parked,
}

/// <summary>
/// Una ventana de una escena guardada. No guarda el handle (cambia al reabrir la app), sino cómo
/// reconocerla: la app (AUMID o ejecutable) y una pista de título.
/// </summary>
/// <param name="PanelId">Panel de la escena al que pertenece.</param>
public sealed record SceneWindow(string AppKey, string Label, string TitleHint, string? PanelId, ScenePlacement Placement)
{
    /// <summary>Columnas que ocupa en un panel en tiles (ADR 0015).</summary>
    public TileSize Size { get; init; } = TileSize.Small;

    /// <summary>Formato anterior (sin paneles): "Context" o "Work". Sólo para migrar escenas viejas.</summary>
    public string? Home { get; init; }

    /// <summary>Formato anterior: monitor auxiliar por orden. Sólo para migrar escenas viejas.</summary>
    public int? AuxIndex { get; init; }
}

/// <summary>Un panel de una escena guardada.</summary>
/// <param name="AuxIndex">
/// Monitor auxiliar, contado de izquierda a derecha desde 1, o <c>null</c> para el principal. No se usa
/// la clave del monitor porque otra persona u otro equipo tendrá otros monitores.
/// </param>
public sealed record ScenePanel(
    string Id, string Name, int? AuxIndex, PanelSide Side, PanelTemplate Template, PanelSlot Slot = PanelSlot.Upper, double Height = 1);

/// <summary>Template de un panel en el formato anterior de las escenas (antes de los paneles).</summary>
public sealed record ScenePanelTemplate(int? AuxIndex, string Home, PanelTemplate Template);

/// <summary>Una actividad con nombre: qué paneles, qué ventanas y en qué panel va cada una.</summary>
public sealed record SavedScene(string Name, IReadOnlyList<SceneWindow> Windows)
{
    public IReadOnlyList<ScenePanel> Panels { get; init; } = [];

    /// <summary>Vista del principal (ADR 0012). Las escenas anteriores son "foco al centro".</summary>
    public FocusPosition View { get; init; }

    /// <summary>Proporciones de las columnas en esa vista; <c>null</c> en las escenas anteriores.</summary>
    public ViewWeights? Weights { get; init; }

    /// <summary>Formato anterior. Sólo para migrar escenas viejas.</summary>
    public IReadOnlyList<ScenePanelTemplate> Templates { get; init; } = [];

    /// <summary>
    /// Convierte una escena del formato anterior (Contexto/Trabajo + monitor auxiliar por orden) al de
    /// paneles. Una escena ya convertida se devuelve tal cual.
    /// </summary>
    public SavedScene Upgrade()
    {
        if (Panels.Count > 0 || Windows.All(w => w.PanelId is not null))
        {
            return this;
        }

        static string IdOf(string? home, int? aux) =>
            (home, aux) switch
            {
                ("Context", null) => Workspace.ContextPanel,
                (_, null) => Workspace.WorkPanel,
                ("Context", { } i) => $"aux{i}-contexto",
                (_, { } i) => $"aux{i}-trabajo",
            };

        var panels = new List<ScenePanel>
        {
            new(Workspace.ContextPanel, Strings.Panel_Context, null, PanelSide.Left, PanelTemplate.Tiles),
            new(Workspace.WorkPanel, Strings.Panel_Work, null, PanelSide.Right, PanelTemplate.Tiles),
        };
        foreach (var aux in Windows.Where(w => w.AuxIndex is not null).Select(w => (w.Home, w.AuxIndex)).Distinct())
        {
            bool context = aux.Home == "Context";
            panels.Add(new ScenePanel(IdOf(aux.Home, aux.AuxIndex), context ? Strings.Panel_Context : Strings.Panel_Work, aux.AuxIndex,
                context ? PanelSide.Left : PanelSide.Right, PanelTemplate.Tiles));
        }

        foreach (var t in Templates)
        {
            int i = panels.FindIndex(p => p.Id == IdOf(t.Home, t.AuxIndex));
            if (i >= 0 && (t.AuxIndex is not null || t.Template != PanelTemplate.Fill))
            {
                panels[i] = panels[i] with { Template = t.Template };
            }
        }

        return this with
        {
            Windows = Windows.Select(w => w with { PanelId = IdOf(w.Home, w.AuxIndex), Home = null, AuxIndex = null }).ToList(),
            Panels = panels,
            Templates = [],
        };
    }
}

/// <summary>Una ventana abierta, tal como la ve el emparejador.</summary>
public sealed record OpenWindow(nint Handle, string AppKey, string Title);

/// <summary>Resultado de emparejar una escena con las ventanas abiertas.</summary>
public sealed record SceneMatch(IReadOnlyList<(SceneWindow Entry, OpenWindow Window)> Matched, IReadOnlyList<SceneWindow> Missing);

/// <summary>Una ventana tal como queda al aplicar una escena (ya con handle).</summary>
public sealed record SceneEntry(nint Handle, string Label, string PanelId, ScenePlacement Placement)
{
    public TileSize Size { get; init; } = TileSize.Small;
}

public static class SceneMatcher
{
    /// <summary>
    /// Empareja cada ventana de la escena con una abierta de la misma app. Si hay varias, prefiere el
    /// mismo título, luego uno que contenga la pista (o al revés) y, si no, la primera en orden Z.
    /// Cada ventana abierta se usa una sola vez.
    /// </summary>
    /// <param name="strict">
    /// Para la sesión, que se aplica sola al arrancar (no la elige el usuario): una ventana de la misma app
    /// con otro título sólo se toma si ninguna de esa app casó por título (tras reiniciar cambian todos) y
    /// cuadran uno a uno (tantas entradas como ventanas libres de esa app). Así una ventana cualquiera no
    /// ocupa el sitio de otra que aún no se abrió; y tras reiniciar, cada una vuelve a su panel.
    /// </param>
    public static SceneMatch Match(SavedScene scene, IReadOnlyList<OpenWindow> open, bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(open);
        var used = new HashSet<nint>();
        var matched = new List<(SceneWindow, OpenWindow)>();
        var missing = new List<SceneWindow>();

        // Primero las de título exacto, para que una pista vaga no le quite la ventana a otra precisa.
        var ordered = scene.Windows
            .Select((w, i) => (w, i))
            .OrderByDescending(x => open.Any(o => SameApp(o, x.w) && Same(o.Title, x.w.TitleHint)))
            .ToList();
        var byEntry = new Dictionary<int, OpenWindow>();
        foreach (var (entry, index) in ordered)
        {
            var candidates = open.Where(o => !used.Contains(o.Handle) && SameApp(o, entry)).ToList();
            var pick = candidates.FirstOrDefault(o => Same(o.Title, entry.TitleHint))
                ?? candidates.FirstOrDefault(o => Overlaps(o.Title, entry.TitleHint))
                ?? (strict ? null : candidates.FirstOrDefault());
            if (pick is null)
            {
                continue;
            }

            used.Add(pick.Handle);
            byEntry[index] = pick;
        }

        if (strict)
        {
            var pending = Enumerable.Range(0, scene.Windows.Count).Where(i => !byEntry.ContainsKey(i)).ToList();
            foreach (var app in pending.GroupBy(i => scene.Windows[i].AppKey, StringComparer.OrdinalIgnoreCase))
            {
                // Si alguna ventana de esa app sí casó por título, los títulos no cambiaron: las que faltan
                // de verdad no están abiertas.
                bool someMatchedByTitle = byEntry.Keys.Any(i => string.Equals(scene.Windows[i].AppKey, app.Key, StringComparison.OrdinalIgnoreCase));
                var free = open.Where(o => !used.Contains(o.Handle) && SameApp(o, scene.Windows[app.First()])).ToList();
                if (!someMatchedByTitle && free.Count == app.Count())
                {
                    foreach (var (index, window) in app.Zip(free))
                    {
                        used.Add(window.Handle);
                        byEntry[index] = window;
                    }
                }
            }
        }

        for (int i = 0; i < scene.Windows.Count; i++)
        {
            if (byEntry.TryGetValue(i, out var window))
            {
                matched.Add((scene.Windows[i], window));
            }
            else
            {
                missing.Add(scene.Windows[i]);
            }
        }

        return new SceneMatch(matched, missing);
    }

    private static bool SameApp(OpenWindow o, SceneWindow w) => string.Equals(o.AppKey, w.AppKey, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Overlaps(string title, string hint) =>
        hint.Length > 0 && title.Length > 0 &&
        (title.Contains(hint, StringComparison.OrdinalIgnoreCase) || hint.Contains(title, StringComparison.OrdinalIgnoreCase));
}
