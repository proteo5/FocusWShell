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

    /// <summary>
    /// Lista tipo Explorador (ADR 0015): una ventana de FocusWShell ocupa el panel y muestra sus apps;
    /// las ventanas quedan detrás de ella, sin minimizar, para que sus miniaturas sigan en vivo.
    /// </summary>
    List,
}

/// <summary>Modo de la lista tipo Explorador (ADR 0015).</summary>
public enum ListLayout
{
    /// <summary>Iconos grandes con miniatura en vivo.</summary>
    LargeIcons,

    /// <summary>Icono y nombre, en varias columnas.</summary>
    List,

    /// <summary>Icono, nombre, título y estado.</summary>
    Details,
}

/// <summary>Lado del Centro en el que está un panel. Define cuál es su borde exterior.</summary>
public enum PanelSide
{
    Left,
    Right,
}

/// <summary>
/// Disposición del monitor principal (ADR 0012 y su enmienda): qué columnas de paneles se ven y en qué orden.
/// El panel del lado izquierdo (Contexto) queda siempre en un borde del monitor y el del lado derecho
/// (Trabajo), junto al foco. Las reglas de los botones están en <see cref="MainArrangement"/>.
/// </summary>
public enum FocusPosition
{
    /// <summary>Contexto · foco · Trabajo.</summary>
    Center,

    /// <summary>Foco · Trabajo · Contexto (Contexto prestado a la derecha; sólo en un ultrawide).</summary>
    Left,

    /// <summary>Contexto · Trabajo · foco (Trabajo prestado a la izquierda; sólo en un ultrawide).</summary>
    Right,

    /// <summary>Contexto · foco: la columna de Trabajo, apagada.</summary>
    ContextOnly,

    /// <summary>Foco · Trabajo: la columna de Contexto, apagada.</summary>
    WorkOnly,

    /// <summary>Solo foco: las dos columnas, apagadas.</summary>
    FocusOnly,
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
        int Two(double a, double b) => (int)Math.Round(100 * a / (a + b));
        return focus switch
        {
            FocusPosition.Left => Text.F(Strings.Weights_FocusLeft, Pct(Center), Pct(Right), Pct(Left)),
            FocusPosition.Right => Text.F(Strings.Weights_FocusRight, Pct(Left), Pct(Right), Pct(Center)),
            FocusPosition.ContextOnly => Text.F(Strings.Weights_ContextOnly, Two(Left, Center), Two(Center, Left)),
            FocusPosition.WorkOnly => Text.F(Strings.Weights_WorkOnly, Two(Center, Right), Two(Right, Center)),
            FocusPosition.FocusOnly => Strings.Weights_FocusOnly,
            _ => Text.F(Strings.Weights_FocusCenter, Pct(Left), Pct(Center), Pct(Right)),
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

/// <summary>
/// Proporciones de partida de cada disposición (ADR 0012 y su enmienda). Los pesos de una columna apagada
/// no se usan, pero se guardan positivos para que los pesos sigan siendo válidos.
/// </summary>
public static class ViewProportions
{
    public static ViewWeights Default(FocusPosition focus) => focus switch
    {
        FocusPosition.Center => new(18, 55, 27),
        FocusPosition.ContextOnly => new(35, 65, 27),
        FocusPosition.WorkOnly => new(18, 65, 35),
        FocusPosition.FocusOnly => new(18, 55, 27),
        _ => new(15, 55, 30),
    };
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
public sealed record LayoutWindow(nint Handle, string Label, PixelSize? MinSize = null)
{
    /// <summary>Columnas que ocupa en un panel en tiles (ADR 0015).</summary>
    public TileSize TileSize { get; init; } = TileSize.Small;
}

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

    /// <summary>
    /// Su parte del alto de la columna cuando la comparte con otro panel (relativa a la del otro). Por
    /// defecto, a partes iguales.
    /// </summary>
    public double Height { get; init; } = 1;

    /// <summary>En tiles: primera fila que se ve si no caben todas (ADR 0015).</summary>
    public int ScrollRow { get; init; }

    /// <summary>En tiles: ventana que hay que mostrar; el panel se desplaza hasta su fila.</summary>
    public nint? Reveal { get; init; }

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
    /// <summary>Ancho mínimo de una columna de tiles, en píxeles físicos; 0 = una sola columna.</summary>
    public int MinCellWidth { get; init; }

    /// <summary>Alto mínimo de una fila de tiles, en píxeles físicos.</summary>
    public int MinRowHeight { get; init; }

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

    /// <summary>Filas de los paneles en tiles que no caben enteros, por nombre.</summary>
    public IReadOnlyDictionary<string, TilePages> Pages { get; init; } = new Dictionary<string, TilePages>();

    /// <summary>Área (bajo su franja) de cada panel en lista: ahí va la ventana de la lista.</summary>
    public IReadOnlyDictionary<string, PixelRect> Lists { get; init; } = new Dictionary<string, PixelRect>();
}
