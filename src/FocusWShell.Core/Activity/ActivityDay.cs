namespace FocusWShell.Core.Activity;

/// <summary>El tiempo de un sitio dentro de un navegador en un día.</summary>
/// <param name="Site">Su host, o <see cref="Browsers.SiteHost.Private"/> para las ventanas privadas.</param>
public sealed record ActivitySiteTotal(string Site, TimeSpan InFocus, TimeSpan InUse);

/// <summary>El tiempo de una app en un día: en el foco y en uso (ADR 0019).</summary>
/// <param name="Sites">En un navegador, sus sitios, del más usado al menos (vacío si no se midieron).</param>
public sealed record ActivityAppTotal(string Key, string Name, string? PanelId, TimeSpan InFocus, TimeSpan InUse, IReadOnlyList<ActivitySiteTotal> Sites);

/// <summary>El resumen de un día de actividad, calculado a partir de sus tramos.</summary>
/// <param name="Active">Tiempo presente (con algo en el foco o en uso).</param>
/// <param name="SideBySide">Tiempo con el foco en una app y el uso en otra.</param>
/// <param name="ToFocus">Cuántas veces llegó una app al foco.</param>
/// <param name="LongestFocus">La racha más larga con la misma app en el foco, sin pausas.</param>
/// <param name="Apps">Por app, de la más usada a la menos (foco + uso).</param>
public sealed record ActivityDay(
    TimeSpan Active,
    TimeSpan SideBySide,
    int ToFocus,
    TimeSpan LongestFocus,
    string? LongestFocusApp,
    IReadOnlyList<ActivityAppTotal> Apps)
{
    public static ActivityDay Empty { get; } = new(TimeSpan.Zero, TimeSpan.Zero, 0, TimeSpan.Zero, null, []);

    public static ActivityDay From(IEnumerable<ActivitySegment> segments)
    {
        var active = TimeSpan.Zero;
        var side = TimeSpan.Zero;
        var toFocus = 0;
        var longest = TimeSpan.Zero;
        string? longestApp = null;
        var apps = new Dictionary<string, (string Name, string? PanelId, TimeSpan Focus, TimeSpan Use)>(StringComparer.OrdinalIgnoreCase);
        var sites = new Dictionary<string, Dictionary<string, (TimeSpan Focus, TimeSpan Use)>>(StringComparer.OrdinalIgnoreCase);

        ActivityApp? lastFocus = null; // la última app que hubo en el foco, aunque luego quedara vacío
        DateTime? streakEnd = null;
        var streak = TimeSpan.Zero;
        foreach (var s in segments.OrderBy(s => s.Start))
        {
            active += s.Duration;
            if (s.SideBySide)
            {
                side += s.Duration;
            }

            if (s.InUse is { } use)
            {
                Count(use, s.Duration, inFocus: false);
            }

            if (s.Focus is not { } focus)
            {
                streakEnd = null; // el foco quedó vacío: la racha se corta
                continue;
            }

            Count(focus, s.Duration, inFocus: true);

            // Llegó al foco: otra app que la última (tras una pausa, la misma no cuenta otra vez).
            // Otro sitio en el mismo navegador no es otra llegada.
            if (!focus.SameApp(lastFocus))
            {
                toFocus++;
            }

            // La racha sigue si es la misma app y sin hueco (una pausa por inactividad la corta).
            streak = focus.SameApp(lastFocus) && streakEnd == s.Start ? streak + s.Duration : s.Duration;
            streakEnd = s.End;
            lastFocus = focus;
            if (streak > longest)
            {
                longest = streak;
                longestApp = focus.Name;
            }
        }

        var totals = apps
            .Select(a => new ActivityAppTotal(a.Key, a.Value.Name, a.Value.PanelId, a.Value.Focus, a.Value.Use, SitesOf(a.Key)))
            .OrderByDescending(a => a.InFocus + a.InUse)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new ActivityDay(active, side, toFocus, longest, longestApp, totals);

        void Count(ActivityApp app, TimeSpan duration, bool inFocus)
        {
            var focusPart = inFocus ? duration : TimeSpan.Zero;
            var usePart = inFocus ? TimeSpan.Zero : duration;
            var c = apps.TryGetValue(app.Key, out var found) ? found : (Name: app.Name, PanelId: app.PanelId, Focus: TimeSpan.Zero, Use: TimeSpan.Zero);
            // El nombre y el panel, los últimos que tuvo en el día.
            apps[app.Key] = (app.Name, app.PanelId ?? c.PanelId, c.Focus + focusPart, c.Use + usePart);

            if (app.Site is { } site)
            {
                if (!sites.TryGetValue(app.Key, out var bySite))
                {
                    sites[app.Key] = bySite = new(StringComparer.OrdinalIgnoreCase);
                }

                var t = bySite.GetValueOrDefault(site);
                bySite[site] = (t.Focus + focusPart, t.Use + usePart);
            }
        }

        List<ActivitySiteTotal> SitesOf(string key) => sites.TryGetValue(key, out var bySite)
            ?
            [
                .. bySite.Select(s => new ActivitySiteTotal(s.Key, s.Value.Focus, s.Value.Use))
                    .OrderByDescending(s => s.InFocus + s.InUse)
                    .ThenBy(s => s.Site, StringComparer.OrdinalIgnoreCase),
            ]
            : [];
    }
}
