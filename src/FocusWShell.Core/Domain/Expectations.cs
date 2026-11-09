using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Domain;

/// <summary>Estado de una ventana leído del sistema, ya estabilizado (sin eventos recientes).</summary>
/// <param name="Visible">Rectángulo visible (sin bordes invisibles). Sin significado si está minimizada.</param>
public sealed record ObservedWindow(nint Handle, bool Exists, bool Minimized, bool Maximized, PixelRect Visible);

/// <summary>Lo que el gestor dejó en una ventana la última vez que la tocó.</summary>
public abstract record Expectation;

/// <summary>La ventana debe estar visible en este rectángulo.</summary>
public sealed record ExpectVisible(PixelRect Rect) : Expectation;

/// <summary>La ventana debe estar minimizada (oculta en una pila o estacionada).</summary>
public sealed record ExpectMinimized : Expectation;

/// <summary>Qué hizo alguien que no es el gestor con una ventana gestionada.</summary>
public enum ExternalChange
{
    /// <summary>Está como el gestor la dejó: los eventos eran suyos.</summary>
    None,

    Closed,

    /// <summary>Se minimizó (botón de Windows o barra de tareas). En el modelo, <c>Park</c>.</summary>
    Minimized,

    /// <summary>Estaba minimizada por el gestor y alguien la mostró. En el modelo, <c>Restore</c>.</summary>
    Shown,

    /// <summary>Se maximizó con el botón de Windows: queda fuera de layout.</summary>
    Maximized,

    /// <summary>Se arrastró o redimensionó: queda fuera de layout.</summary>
    MovedOrResized,
}

/// <summary>
/// Registro de geometrías esperadas (S4). Distingue los cambios del gestor de los del usuario
/// comparando el estado final de cada ventana con lo que el gestor dejó en ella, en lugar de
/// atribuir cada evento: con movimiento asíncrono hay eventos intermedios (normalizar, mover,
/// corregir) que no coinciden con el destino pero son propios.
/// Seguro entre hilos: los eventos llegan en el hilo del observador.
/// </summary>
public sealed class ExpectationRegistry
{
    /// <summary>Diferencia máxima por borde que se sigue considerando "como el gestor la dejó".</summary>
    public const int Tolerance = 1;

    private readonly Lock _gate = new();
    private readonly Dictionary<nint, Expectation> _expected = [];

    public void Expect(nint handle, Expectation expectation)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        lock (_gate)
        {
            _expected[handle] = expectation;
        }
    }

    public void Forget(nint handle)
    {
        lock (_gate)
        {
            _expected.Remove(handle);
        }
    }

    public bool IsWatched(nint handle)
    {
        lock (_gate)
        {
            return _expected.ContainsKey(handle);
        }
    }

    public Expectation? Get(nint handle)
    {
        lock (_gate)
        {
            return _expected.GetValueOrDefault(handle);
        }
    }

    /// <summary>
    /// Compara el estado observado con el esperado. Si hubo un cambio externo, la expectativa pasa a
    /// ser lo observado, para no volver a informar del mismo cambio (el usuario manda).
    /// </summary>
    public ExternalChange Reconcile(ObservedWindow observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        lock (_gate)
        {
            if (!_expected.TryGetValue(observed.Handle, out var expected))
            {
                return ExternalChange.None;
            }

            var change = Classify(expected, observed);
            if (change == ExternalChange.Closed)
            {
                _expected.Remove(observed.Handle);
            }
            else if (change != ExternalChange.None)
            {
                _expected[observed.Handle] = observed.Minimized
                    ? new ExpectMinimized()
                    : new ExpectVisible(observed.Visible);
            }

            return change;
        }
    }

    public static ExternalChange Classify(Expectation expected, ObservedWindow observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        if (!observed.Exists)
        {
            return ExternalChange.Closed;
        }

        return expected switch
        {
            ExpectMinimized => observed.Minimized ? ExternalChange.None : ExternalChange.Shown,
            ExpectVisible v when observed.Minimized => ExternalChange.Minimized,
            ExpectVisible v when observed.Maximized => ExternalChange.Maximized,
            ExpectVisible v => Differs(v.Rect, observed.Visible) ? ExternalChange.MovedOrResized : ExternalChange.None,
            _ => throw new ArgumentException($"Expectativa desconocida: {expected.GetType().Name}", nameof(expected)),
        };
    }

    private static bool Differs(PixelRect a, PixelRect b) =>
        Math.Abs(a.Left - b.Left) > Tolerance || Math.Abs(a.Top - b.Top) > Tolerance ||
        Math.Abs(a.Right - b.Right) > Tolerance || Math.Abs(a.Bottom - b.Bottom) > Tolerance;
}
