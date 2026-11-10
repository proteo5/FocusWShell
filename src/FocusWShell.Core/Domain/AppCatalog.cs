namespace FocusWShell.Core.Domain;

/// <summary>
/// Una app que FocusWShell ha visto alguna vez (ADR 0011). Guarda cómo reconocerla y cómo abrirla,
/// nunca títulos de ventanas.
/// </summary>
/// <param name="AppKey">Clave de la app: AUMID o ejecutable (la misma que los tamaños mínimos y las escenas).</param>
/// <param name="Name">Nombre visible; el usuario puede cambiarlo.</param>
/// <param name="ExecutablePath">Ruta del ejecutable, para abrirla. Puede faltar.</param>
/// <param name="Aumid">AUMID de la app empaquetada, para abrirla por <c>shell:AppsFolder</c>. Puede faltar.</param>
/// <param name="LastPanelId">Último panel en el que estuvo; es el destino sugerido al abrirla.</param>
/// <param name="Hidden">Si el usuario la ocultó de la lista.</param>
public sealed record CatalogApp(
    string AppKey,
    string Name,
    string? ExecutablePath,
    string? Aumid,
    string? LastPanelId,
    bool Hidden,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen)
{
    /// <summary>Si hay forma de abrirla.</summary>
    public bool CanLaunch => !string.IsNullOrEmpty(Aumid) || !string.IsNullOrEmpty(ExecutablePath);

    /// <summary>Si el usuario le puso el nombre: entonces el gestor no lo vuelve a cambiar.</summary>
    public bool Renamed { get; init; }
}

/// <summary>Catálogo de apps que se llena por detección suave: nunca se escanea lo instalado.</summary>
public sealed class AppCatalog
{
    private readonly Dictionary<string, CatalogApp> _apps = new(StringComparer.OrdinalIgnoreCase);

    public AppCatalog()
    {
    }

    public AppCatalog(IEnumerable<CatalogApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        foreach (var app in apps)
        {
            _apps[app.AppKey] = app;
        }
    }

    /// <summary>Las apps, por nombre.</summary>
    public IReadOnlyList<CatalogApp> Apps => [.. _apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];

    public CatalogApp? Find(string appKey) => _apps.GetValueOrDefault(appKey);

    /// <summary>
    /// Registra una ventana vista. Si la app es nueva, la añade; si ya existía, actualiza la última vez
    /// vista y completa cómo abrirla si antes no se sabía. Devuelve si cambió algo que valga guardar.
    /// </summary>
    public bool Observe(string appKey, string name, string? executablePath, string? aumid, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        if (!_apps.TryGetValue(appKey, out var app))
        {
            _apps[appKey] = new CatalogApp(appKey, name, Empty(executablePath), Empty(aumid), null, false, now, now);
            return true;
        }

        var updated = app with
        {
            ExecutablePath = app.ExecutablePath ?? Empty(executablePath),
            Aumid = app.Aumid ?? Empty(aumid),
            LastSeen = now,
        };
        _apps[appKey] = updated;

        // La hora de "última vez vista" sola no justifica escribir el archivo a cada refresco.
        return updated with { LastSeen = app.LastSeen } != app;
    }

    /// <summary>Recuerda el panel en el que se puso la app. Devuelve si cambió.</summary>
    public bool RememberPanel(string appKey, string panelId)
    {
        if (!_apps.TryGetValue(appKey, out var app) || app.LastPanelId == panelId)
        {
            return false;
        }

        _apps[appKey] = app with { LastPanelId = panelId };
        return true;
    }

    public bool Rename(string appKey, string name)
    {
        if (!_apps.TryGetValue(appKey, out var app) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        _apps[appKey] = app with { Name = name.Trim(), Renamed = true };
        return true;
    }

    /// <summary>
    /// Mejora el nombre automático de una app (p. ej. "Application Frame Host" → "Calculator") si el usuario
    /// no la renombró y su nombre es <paramref name="previous"/>. Devuelve si cambió.
    /// </summary>
    public bool ImproveName(string appKey, string previous, string better)
    {
        if (!_apps.TryGetValue(appKey, out var app) || app.Renamed || string.IsNullOrWhiteSpace(better)
            || app.Name != previous || app.Name == better.Trim())
        {
            return false;
        }

        _apps[appKey] = app with { Name = better.Trim() };
        return true;
    }

    public bool SetHidden(string appKey, bool hidden)
    {
        if (!_apps.TryGetValue(appKey, out var app) || app.Hidden == hidden)
        {
            return false;
        }

        _apps[appKey] = app with { Hidden = hidden };
        return true;
    }

    /// <summary>Olvida una app. Si vuelve a aparecer, se registra de nuevo.</summary>
    public bool Forget(string appKey) => _apps.Remove(appKey);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
