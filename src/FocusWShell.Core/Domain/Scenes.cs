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
/// <param name="AuxIndex">
/// Monitor auxiliar, contado de izquierda a derecha desde 1, o <c>null</c> para el principal. No se usa
/// <c>\\.\DISPLAYn</c> porque cambia al reconectar.
/// </param>
public sealed record SceneWindow(string AppKey, string Label, string TitleHint, Home Home, ScenePlacement Placement, int? AuxIndex);

/// <summary>Template de un panel en una escena guardada.</summary>
public sealed record ScenePanelTemplate(int? AuxIndex, Home Home, PanelTemplate Template);

/// <summary>Una actividad con nombre: qué ventanas, en qué panel y con qué templates.</summary>
public sealed record SavedScene(string Name, IReadOnlyList<SceneWindow> Windows, IReadOnlyList<ScenePanelTemplate> Templates);

/// <summary>Una ventana abierta, tal como la ve el emparejador.</summary>
public sealed record OpenWindow(nint Handle, string AppKey, string Title);

/// <summary>Resultado de emparejar una escena con las ventanas abiertas.</summary>
public sealed record SceneMatch(IReadOnlyList<(SceneWindow Entry, OpenWindow Window)> Matched, IReadOnlyList<SceneWindow> Missing);

/// <summary>Una ventana tal como queda al aplicar una escena (ya con handle y monitor resueltos).</summary>
public sealed record SceneEntry(nint Handle, string Label, Home Home, ScenePlacement Placement, string? Display);

public static class SceneMatcher
{
    /// <summary>
    /// Empareja cada ventana de la escena con una abierta de la misma app. Si hay varias, prefiere el
    /// mismo título, luego uno que contenga la pista (o al revés) y, si no, la primera en orden Z.
    /// Cada ventana abierta se usa una sola vez.
    /// </summary>
    public static SceneMatch Match(SavedScene scene, IReadOnlyList<OpenWindow> open)
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
                ?? candidates.FirstOrDefault();
            if (pick is null)
            {
                continue;
            }

            used.Add(pick.Handle);
            byEntry[index] = pick;
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
