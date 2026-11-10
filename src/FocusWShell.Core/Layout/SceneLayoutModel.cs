using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Cómo presenta un panel sus ventanas. Ver docs/02-modelo-de-dominio.md.</summary>
public enum PanelTemplate
{
    /// <summary>Todas visibles, una por fila.</summary>
    Tiles,

    /// <summary>Sólo la primera visible; el resto minimizadas.</summary>
    Stack,

    /// <summary>
    /// Sólo en monitores auxiliares (ADR 0008): la primera ocupa el monitor entero, maximizada; el
    /// resto del panel, y los demás paneles de ese monitor, se minimizan.
    /// </summary>
    Fill,
}

/// <summary>Lado del Centro en el que está un panel. Define cuál es su borde exterior.</summary>
public enum PanelSide
{
    Left,
    Right,
}

/// <summary>
/// Vista del monitor principal (ADR 0012): dónde va el foco. Las columnas de paneles van, de izquierda a
/// derecha: al centro, izquierda · foco · derecha; a la izquierda, foco · derecha · izquierda; a la
/// derecha, izquierda · derecha · foco. El panel del lado izquierdo (Contexto) queda siempre en un borde
/// del monitor y el del lado derecho (Trabajo), junto al foco.
/// </summary>
public enum FocusPosition
{
    Center,
    Left,
    Right,
}

/// <summary>
/// Pesos de las columnas del principal: la izquierda (Contexto), el foco y la derecha (Trabajo). Son
/// relativos; no tienen que sumar 100.
/// </summary>
public sealed record ViewWeights(double Left, double Center, double Right)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Valid(Left) && Valid(Center) && Valid(Right);

    /// <summary>En porcentajes enteros, para mostrarlos (p. ej. "18 · 55 · 27").</summary>
    public string Describe(FocusPosition focus)
    {
        double total = Left + Center + Right;
        int Pct(double w) => (int)Math.Round(100 * w / total);
        return focus switch
        {
            FocusPosition.Left => $"foco {Pct(Center)} · derecha {Pct(Right)} · izquierda {Pct(Left)} %",
            FocusPosition.Right => $"izquierda {Pct(Left)} · derecha {Pct(Right)} · foco {Pct(Center)} %",
            _ => $"izquierda {Pct(Left)} · foco {Pct(Center)} · derecha {Pct(Right)} %",
        };
    }

    /// <summary>Si son las mismas proporciones (a menos de medio punto porcentual).</summary>
    public bool SameAs(ViewWeights other)
    {
        ArgumentNullException.ThrowIfNull(other);
        double a = Left + Center + Right, b = other.Left + other.Center + other.Right;
        return Math.Abs((Left / a) - (other.Left / b)) < 0.005
            && Math.Abs((Center / a) - (other.Center / b)) < 0.005
            && Math.Abs((Right / a) - (other.Right / b)) < 0.005;
    }

    private static bool Valid(double w) => w > 0 && !double.IsNaN(w) && !double.IsInfinity(w);
}

/// <summary>Proporciones de partida y presets de cada vista (ADR 0012).</summary>
public static class ViewProportions
{
    public const string Normal = "Normal";
    public const string WideFocus = "Foco ancho";
    public const string HalfAndHalf = "Mitad y mitad";

    /// <summary>Las de la tabla del ADR 0012.</summary>
    public static ViewWeights Default(FocusPosition focus) => focus switch
    {
        FocusPosition.Center => new(18, 55, 27),
        _ => new(15, 55, 30),
    };

    /// <summary>
    /// Presets de la vista: el normal, uno con el foco más ancho y uno con el foco y el panel de al lado
    /// (Trabajo) igual de grandes, para comparar dos ventanas.
    /// </summary>
    public static IReadOnlyList<(string Name, ViewWeights Weights)> Presets(FocusPosition focus) => focus switch
    {
        FocusPosition.Center =>
        [
            (Normal, Default(focus)),
            (WideFocus, new(12, 70, 18)),
            (HalfAndHalf, new(10, 45, 45)),
        ],
        _ =>
        [
            (Normal, Default(focus)),
            (WideFocus, new(10, 70, 20)),
            (HalfAndHalf, new(10, 45, 45)),
        ],
    };

    /// <summary>Nombre del preset que coincide con estos pesos, o <c>null</c> si son personalizados.</summary>
    public static string? PresetName(FocusPosition focus, ViewWeights weights) =>
        Presets(focus).FirstOrDefault(p => p.Weights.SameAs(weights)).Name;
}

/// <summary>
/// Puesto de un panel dentro de su columna (ADR 0011): como máximo dos paneles por columna, uno
/// encima del otro. Un panel solo en su columna la ocupa entera, sea cual sea su puesto.
/// </summary>
public enum PanelSlot
{
    Upper,
    Lower,
}

/// <summary>Una ventana a colocar, con su tamaño mínimo conocido (rectángulo visible), si lo hay.</summary>
public sealed record LayoutWindow(nint Handle, string Label, PixelSize? MinSize = null);

/// <summary>Un panel lateral y sus ventanas, en orden (la primera es la activa en modo pila).</summary>
/// <param name="OuterEdgeShared">
/// Si el borde exterior del panel limita con otro monitor. Entonces el desborde hacia fuera caería en
/// ese monitor: el Centro puede encoger más y, si aun así no cabe, la ventana sale del layout.
/// </param>
public sealed record PanelInput(
    string Name,
    PanelSide Side,
    double Weight,
    PanelTemplate Template,
    IReadOnlyList<LayoutWindow> Windows,
    bool OuterEdgeShared = false)
{
    /// <summary>Nombre visible del panel, para las notas. Si no se da, el identificador (<see cref="Name"/>).</summary>
    public string? Title { get; init; }

    /// <summary>Arriba o abajo en su columna, si la comparte con otro panel.</summary>
    public PanelSlot Slot { get; init; }

    public string DisplayTitle => Title ?? Name;
}

/// <summary>Escena de tres columnas: panel izquierdo | Centro | panel derecho.</summary>
/// <param name="MinCenterFraction">
/// Fracción del ancho de trabajo por debajo de la cual los paneles no pueden encoger el Centro
/// para hacer sitio a sus ventanas. Por defecto, la mitad.
/// </param>
/// <param name="PanelTopInset">
/// Alto, en píxeles físicos, de la franja reservada arriba de cada panel para su tira de pestañas.
/// Las ventanas de los paneles empiezan debajo; el Centro no tiene franja.
/// </param>
/// <param name="SharedEdgeMinCenterFraction">
/// Piso del Centro frente a un panel cuyo borde exterior da a otro monitor: el Centro baja hasta el
/// mínimo de su ventana, pero nunca por debajo de esta fracción (por si ese mínimo no se conoce).
/// </param>
public sealed record SceneInput(
    PixelRect WorkArea,
    PanelInput Left,
    double CenterWeight,
    LayoutWindow? Center,
    PanelInput Right,
    int Gap = 0,
    double MinCenterFraction = 0.5,
    double SharedEdgeMinCenterFraction = 0.3,
    int PanelTopInset = 0)
{
    /// <summary>Segundo panel de la columna izquierda (debajo o encima de <see cref="Left"/>, según su puesto).</summary>
    public PanelInput? LeftLower { get; init; }

    /// <summary>Segundo panel de la columna derecha.</summary>
    public PanelInput? RightLower { get; init; }

    /// <summary>
    /// Dónde va el foco. <see cref="Left"/> y <see cref="Right"/> siguen siendo las columnas de cada lado;
    /// la vista sólo cambia su orden en pantalla. <see cref="PanelInput.OuterEdgeShared"/> se refiere al
    /// borde del monitor que la columna toca en esta vista.
    /// </summary>
    public FocusPosition Focus { get; init; }
}

public enum TargetKind
{
    /// <summary>Visible en <see cref="WindowTarget.Rect"/>.</summary>
    Visible,

    /// <summary>Minimizada: oculta en una pila, o fuera del layout porque no cabe sin invadir otro monitor.</summary>
    Minimized,

    /// <summary>Maximizada en el monitor de <see cref="WindowTarget.Rect"/> (template <c>Fill</c>).</summary>
    Maximized,
}

/// <summary>Destino de una ventana: rectángulo visible o minimizada.</summary>
public sealed record WindowTarget(nint Handle, string Label, string Region, TargetKind Kind, PixelRect Rect);

/// <summary>
/// Resultado del motor: columnas efectivas (por lado, no por posición en pantalla), destinos y explicación
/// de cada ajuste.
/// </summary>
/// <param name="EffectiveTemplates">Template aplicado por panel (puede diferir del pedido).</param>
/// <param name="Notes">Una frase por cada decisión que no es la geometría "ideal" (principio de explicabilidad).</param>
public sealed record SceneLayoutResult(
    PixelRect LeftColumn,
    PixelRect CenterColumn,
    PixelRect RightColumn,
    IReadOnlyDictionary<string, PanelTemplate> EffectiveTemplates,
    IReadOnlyList<WindowTarget> Targets,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Rectángulo de cada panel con ventanas, por nombre, incluida su franja de pestañas arriba. Con dos
    /// paneles en una columna, cada uno tiene su banda.
    /// </summary>
    public IReadOnlyDictionary<string, PixelRect> Bands { get; init; } = new Dictionary<string, PixelRect>();

    /// <summary>Alto de la franja de pestañas de cada banda, en píxeles físicos.</summary>
    public int StripHeight { get; init; }
}
