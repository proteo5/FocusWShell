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

/// <summary>Proporciones de partida de cada vista (ADR 0012).</summary>
public static class ViewProportions
{
    /// <summary>Pesos de la columna izquierda (Contexto), del foco y de la derecha (Trabajo).</summary>
    public static (double Left, double Center, double Right) Default(FocusPosition focus) => focus switch
    {
        FocusPosition.Center => (18, 55, 27),
        _ => (15, 55, 30),
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
