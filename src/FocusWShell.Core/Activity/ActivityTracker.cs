namespace FocusWShell.Core.Activity;

/// <summary>Una app para la actividad (ADR 0019): su clave (la de paneles y escenas), su nombre y su panel.</summary>
/// <param name="PanelId">Su panel, o <c>null</c> si es libre o no la organiza FocusWShell.</param>
/// <param name="Site">En un navegador, el sitio que mostraba (<see cref="Browsers.SiteHost"/>), si se miden los sitios.</param>
public sealed record ActivityApp(string Key, string Name, string? PanelId = null, string? Site = null)
{
    /// <summary>La misma app, aunque cambie su panel o su nombre.</summary>
    public bool SameApp(ActivityApp? other) => other is not null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Un tramo de tiempo presente con la misma app en el foco y la misma en uso. Nunca cruza la medianoche.
/// </summary>
/// <param name="Focus">La app en el foco, o <c>null</c> si el foco estaba vacío.</param>
/// <param name="InUse">La app que recibía el teclado y el ratón, o <c>null</c> si no se supo.</param>
public sealed record ActivitySegment(DateTime Start, DateTime End, ActivityApp? Focus, ActivityApp? InUse)
{
    public TimeSpan Duration => End - Start;

    /// <summary>En paralelo: el foco en una app y el uso en otra.</summary>
    public bool SideBySide => Focus is not null && InUse is not null && !Focus.SameApp(InUse);
}

/// <summary>
/// Cuenta el tiempo en el foco y en uso (ADR 0019). No sabe de Windows: recibe qué hay en el foco y qué está
/// en uso cada vez que cambia, y cuándo se deja de tocar el equipo; devuelve los tramos que se cierran.
/// Las horas son locales (los días de la actividad son los del reloj de quien la usa).
/// </summary>
public sealed class ActivityTracker
{
    /// <summary>Un tramo más corto no se guarda: un cambio de ventana al pasar con Alt+Tab no es actividad.</summary>
    public static readonly TimeSpan MinSegment = TimeSpan.FromSeconds(1);

    private ActivityApp? _focus;
    private ActivityApp? _inUse;
    private DateTime? _start; // null: inactivo (sin tramo abierto)
    private bool _idle;

    /// <summary>Si hay un tramo abierto.</summary>
    public bool Running => _start is not null;

    /// <summary>Cambió la app en el foco o en uso (o las dos). Si no cambia nada, no corta el tramo.</summary>
    public IReadOnlyList<ActivitySegment> Observe(DateTime now, ActivityApp? focus, ActivityApp? inUse)
    {
        if (Same(_focus, focus) && Same(_inUse, inUse))
        {
            // Mismo estado; sólo puede cambiar el nombre o el panel, que se toman para el tramo siguiente.
            _focus = focus;
            _inUse = inUse;
            return [];
        }

        var closed = Close(now);
        _focus = focus;
        _inUse = inUse;
        if (!_idle)
        {
            _start = now;
        }

        return closed;
    }

    /// <summary>
    /// Se dejó de tocar el equipo, se bloqueó o se suspende: el tramo se cierra en <paramref name="since"/>
    /// (la última entrada), no ahora, para no contar los minutos de espera.
    /// </summary>
    public IReadOnlyList<ActivitySegment> Idle(DateTime since)
    {
        var closed = Close(since);
        _idle = true;
        return closed;
    }

    /// <summary>Se volvió a tocar el equipo: empieza un tramo con lo que hay ahora.</summary>
    public void Active(DateTime now)
    {
        if (_idle || _start is null)
        {
            _idle = false;
            _start = now;
        }
    }

    /// <summary>Cierra el tramo abierto y abre otro igual: para guardar lo medido (al apagar, cada tanto).</summary>
    public IReadOnlyList<ActivitySegment> Flush(DateTime now)
    {
        var closed = Close(now);
        if (!_idle)
        {
            _start = now;
        }

        return closed;
    }

    private List<ActivitySegment> Close(DateTime end)
    {
        if (_start is not { } start)
        {
            return [];
        }

        _start = null;
        if (end <= start)
        {
            return [];
        }

        // Partido en la medianoche: cada tramo va al archivo de su día.
        var segments = new List<ActivitySegment>();
        while (start.Date < end.Date)
        {
            var midnight = start.Date.AddDays(1);
            Add(segments, start, midnight);
            start = midnight;
        }

        Add(segments, start, end);
        return segments;
    }

    private void Add(List<ActivitySegment> segments, DateTime start, DateTime end)
    {
        if (end - start >= MinSegment && (_focus is not null || _inUse is not null))
        {
            segments.Add(new ActivitySegment(start, end, _focus, _inUse));
        }
    }

    /// <summary>La misma app con el mismo sitio: otro sitio en el navegador corta el tramo.</summary>
    private static bool Same(ActivityApp? a, ActivityApp? b) =>
        a is null ? b is null : a.SameApp(b) && string.Equals(a.Site, b!.Site, StringComparison.OrdinalIgnoreCase);
}
