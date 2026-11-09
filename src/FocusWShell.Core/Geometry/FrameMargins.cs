namespace FocusWShell.Core.Geometry;

/// <summary>
/// Bordes invisibles de una ventana: diferencia entre su rectángulo completo
/// (<c>GetWindowRect</c>) y su rectángulo visible (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>).
/// En Windows 10/11 suelen ser unos 7 px a izquierda, derecha y abajo, y 0 arriba.
/// Ver docs/03-arquitectura.md#bordes-invisibles.
/// </summary>
public readonly record struct FrameMargins(int Left, int Top, int Right, int Bottom)
{
    public static FrameMargins Between(PixelRect bounds, PixelRect visible) => new(
        visible.Left - bounds.Left,
        visible.Top - bounds.Top,
        bounds.Right - visible.Right,
        bounds.Bottom - visible.Bottom);

    /// <summary>Rectángulo completo que hay que pedir para que la parte visible quede en <paramref name="visible"/>.</summary>
    public PixelRect Expand(PixelRect visible) => new(
        visible.Left - Left,
        visible.Top - Top,
        visible.Right + Right,
        visible.Bottom + Bottom);

    public override string ToString() => $"{Left},{Top},{Right},{Bottom}";
}
