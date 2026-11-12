using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>
/// La disposición del principal como columnas que se prenden y apagan (ADR 0012, enmienda 2026-11-12).
/// Cada disposición es un <see cref="FocusPosition"/>; aquí están las reglas de los botones del deck y del
/// atajo. «Izquierda» es la columna de Contexto y «derecha» la de Trabajo, por su lado (<see cref="PanelSide"/>),
/// aunque en un ultrawide una de ellas pueda estar «prestada» al otro lado.
/// </summary>
public static class MainArrangement
{
    /// <summary>Un principal es ultrawide si su ancho es al menos esto por su alto (21:9 da 2,33; 16:9, 1,78).</summary>
    public const double UltrawideRatio = 2.1;

    public static bool IsUltrawide(PixelRect area) => area.Height > 0 && (double)area.Width / area.Height >= UltrawideRatio;

    /// <summary>
    /// La disposición que se aplica: fuera de un ultrawide, las de columna prestada se muestran con cada
    /// columna en su lado. Lo guardado no cambia, y vuelve cuando vuelve el ultrawide.
    /// </summary>
    public static FocusPosition Effective(FocusPosition view, bool ultrawide) =>
        !ultrawide && view is FocusPosition.Left or FocusPosition.Right ? FocusPosition.Center : view;

    /// <summary>Si se ve la columna izquierda (Contexto), en su lado.</summary>
    public static bool LeftOn(FocusPosition view) => view is FocusPosition.Center or FocusPosition.Right or FocusPosition.ContextOnly;

    /// <summary>Si se ve la columna derecha (Trabajo), en su lado.</summary>
    public static bool RightOn(FocusPosition view) => view is FocusPosition.Center or FocusPosition.Left or FocusPosition.WorkOnly;

    /// <summary>Si la columna derecha (Trabajo) está prestada a la izquierda: Contexto · Trabajo · foco.</summary>
    public static bool RightBorrowedLeft(FocusPosition view) => view == FocusPosition.Right;

    /// <summary>Si la columna izquierda (Contexto) está prestada a la derecha: foco · Trabajo · Contexto.</summary>
    public static bool LeftBorrowedRight(FocusPosition view) => view == FocusPosition.Left;

    /// <summary>El botón extra de la izquierda (traer Trabajo): sólo en un ultrawide, con la izquierda prendida y la derecha apagada.</summary>
    public static bool CanBorrowToLeft(FocusPosition view, bool ultrawide) =>
        ultrawide && view is FocusPosition.ContextOnly or FocusPosition.Right;

    /// <summary>El botón extra de la derecha (traer Contexto): sólo en un ultrawide, con la derecha prendida y la izquierda apagada.</summary>
    public static bool CanBorrowToRight(FocusPosition view, bool ultrawide) =>
        ultrawide && view is FocusPosition.WorkOnly or FocusPosition.Left;

    /// <summary>
    /// Prende o apaga la izquierda. Apagarla con Trabajo prestado allí apaga las dos; prenderla con Contexto
    /// prestado a la derecha lo devuelve a su lado.
    /// </summary>
    public static FocusPosition ToggleLeft(FocusPosition view) => view switch
    {
        FocusPosition.Center => FocusPosition.WorkOnly,
        FocusPosition.Right => FocusPosition.FocusOnly,
        FocusPosition.ContextOnly => FocusPosition.FocusOnly,
        FocusPosition.WorkOnly => FocusPosition.Center,
        FocusPosition.Left => FocusPosition.Center,
        _ => FocusPosition.ContextOnly,
    };

    /// <summary>Prende o apaga la derecha (en espejo de <see cref="ToggleLeft"/>).</summary>
    public static FocusPosition ToggleRight(FocusPosition view) => view switch
    {
        FocusPosition.Center => FocusPosition.ContextOnly,
        FocusPosition.Left => FocusPosition.FocusOnly,
        FocusPosition.WorkOnly => FocusPosition.FocusOnly,
        FocusPosition.ContextOnly => FocusPosition.Center,
        FocusPosition.Right => FocusPosition.Center,
        _ => FocusPosition.WorkOnly,
    };

    /// <summary>El extra de la izquierda: trae Trabajo junto a Contexto, o lo devuelve (apagado).</summary>
    public static FocusPosition ToggleBorrowToLeft(FocusPosition view) => view switch
    {
        FocusPosition.ContextOnly => FocusPosition.Right,
        FocusPosition.Right => FocusPosition.ContextOnly,
        _ => view,
    };

    /// <summary>El extra de la derecha: trae Contexto junto a Trabajo, o lo devuelve (apagado).</summary>
    public static FocusPosition ToggleBorrowToRight(FocusPosition view) => view switch
    {
        FocusPosition.WorkOnly => FocusPosition.Left,
        FocusPosition.Left => FocusPosition.WorkOnly,
        _ => view,
    };

    /// <summary>Las disposiciones que ofrece el principal, en el orden del atajo.</summary>
    public static IReadOnlyList<FocusPosition> Available(bool ultrawide) => ultrawide
        ? [FocusPosition.Center, FocusPosition.Right, FocusPosition.Left, FocusPosition.ContextOnly, FocusPosition.WorkOnly, FocusPosition.FocusOnly]
        : [FocusPosition.Center, FocusPosition.ContextOnly, FocusPosition.WorkOnly, FocusPosition.FocusOnly];

    /// <summary>La siguiente disposición para el atajo.</summary>
    public static FocusPosition Next(FocusPosition view, bool ultrawide)
    {
        var all = Available(ultrawide);
        int i = Math.Max(0, all.ToList().IndexOf(Effective(view, ultrawide)));
        return all[(i + 1) % all.Count];
    }
}
