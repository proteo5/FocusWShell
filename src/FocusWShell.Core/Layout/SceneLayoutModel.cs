using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Cómo presenta un panel sus ventanas. Ver docs/02-modelo-de-dominio.md.</summary>
public enum PanelTemplate
{
    /// <summary>Todas visibles, una por fila.</summary>
    Tiles,

    /// <summary>Sólo la primera visible; el resto minimizadas.</summary>
    Stack,
}

/// <summary>Lado del Centro en el que está un panel. Define cuál es su borde exterior.</summary>
public enum PanelSide
{
    Left,
    Right,
}

/// <summary>Una ventana a colocar, con su tamaño mínimo conocido (rectángulo visible), si lo hay.</summary>
public sealed record LayoutWindow(nint Handle, string Label, PixelSize? MinSize = null);

/// <summary>Un panel lateral y sus ventanas, en orden (la primera es la activa en modo pila).</summary>
public sealed record PanelInput(
    string Name,
    PanelSide Side,
    double Weight,
    PanelTemplate Template,
    IReadOnlyList<LayoutWindow> Windows);

/// <summary>Escena de tres columnas: panel izquierdo | Centro | panel derecho.</summary>
/// <param name="MinCenterFraction">
/// Fracción del ancho de trabajo por debajo de la cual los paneles no pueden encoger el Centro
/// para hacer sitio a sus ventanas. Por defecto, la mitad.
/// </param>
public sealed record SceneInput(
    PixelRect WorkArea,
    PanelInput Left,
    double CenterWeight,
    LayoutWindow? Center,
    PanelInput Right,
    int Gap = 0,
    double MinCenterFraction = 0.5);

public enum TargetKind
{
    /// <summary>Visible en <see cref="WindowTarget.Rect"/>.</summary>
    Visible,

    /// <summary>Minimizada (oculta en una pila).</summary>
    Minimized,
}

/// <summary>Destino de una ventana: rectángulo visible o minimizada.</summary>
public sealed record WindowTarget(nint Handle, string Label, string Region, TargetKind Kind, PixelRect Rect);

/// <summary>Resultado del motor: columnas efectivas, destinos y explicación de cada ajuste.</summary>
/// <param name="EffectiveTemplates">Template aplicado por panel (puede diferir del pedido).</param>
/// <param name="Notes">Una frase por cada decisión que no es la geometría "ideal" (principio de explicabilidad).</param>
public sealed record SceneLayoutResult(
    PixelRect LeftColumn,
    PixelRect CenterColumn,
    PixelRect RightColumn,
    IReadOnlyDictionary<string, PanelTemplate> EffectiveTemplates,
    IReadOnlyList<WindowTarget> Targets,
    IReadOnlyList<string> Notes);
