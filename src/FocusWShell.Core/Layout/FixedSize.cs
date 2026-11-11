using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>
/// Ventanas de tamaño fijo: se mueven, nunca se redimensionan. Lo son las que no declaran borde
/// redimensionable (<c>WS_THICKFRAME</c>), como NetBird, y las apps que el usuario marca así
/// (<see cref="AppSizeMode.Fixed"/>). Estirarlas las deforma: el motor les reserva su tamaño y después se
/// colocan dentro de su hueco.
/// </summary>
public static class FixedSize
{
    /// <summary>Si una ventana se trata como de tamaño fijo.</summary>
    public static bool IsFixed(bool resizable, AppSizeMode mode) =>
        mode == AppSizeMode.Fixed || (mode == AppSizeMode.Auto && !resizable);

    /// <summary>
    /// Su tamaño propio (rectángulo visible). Si se sabe su rectángulo original (el del diario, antes de
    /// que FocusWShell la tocara), sale de ahí: así se corrige una ventana que ya se estiró. Si no, el actual.
    /// </summary>
    /// <param name="visible">Rectángulo visible actual.</param>
    /// <param name="bounds">Rectángulo completo actual (con los bordes invisibles).</param>
    /// <param name="originalBounds">Rectángulo completo original, si se conoce.</param>
    public static PixelSize Natural(PixelRect visible, PixelRect bounds, PixelRect? originalBounds)
    {
        if (originalBounds is { IsEmpty: false } original)
        {
            int w = original.Width - (bounds.Width - visible.Width);
            int h = original.Height - (bounds.Height - visible.Height);
            if (w > 0 && h > 0)
            {
                return new PixelSize(w, h);
            }
        }

        return new PixelSize(visible.Width, visible.Height);
    }

    /// <summary>El mismo tamaño en otro monitor con otra escala.</summary>
    public static PixelSize Scale(PixelSize size, int fromDpi, int toDpi) =>
        fromDpi <= 0 || toDpi <= 0 || fromDpi == toDpi
            ? size
            : new PixelSize(
                (int)Math.Round(size.Width * (double)toDpi / fromDpi),
                (int)Math.Round(size.Height * (double)toDpi / fromDpi));

    /// <summary>
    /// Coloca las de tamaño fijo dentro del hueco que les dio el motor, sin cambiar su tamaño: arriba y
    /// centradas en horizontal. Una maximizada (template Fill, pantalla completa) queda centrada en su
    /// monitor, sin maximizar. Si no cabe, se queda con su tamaño, pegada a la esquina del hueco, y se avisa.
    /// </summary>
    public static (IReadOnlyList<WindowTarget> Targets, IReadOnlyList<string> Notes) Fit(
        IReadOnlyList<WindowTarget> targets,
        IReadOnlyDictionary<nint, PixelSize> sizes)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(sizes);
        var notes = new List<string>();
        var result = targets.Select(t =>
        {
            if (t.Kind == TargetKind.Minimized || !sizes.TryGetValue(t.Handle, out var size) || size.Width <= 0 || size.Height <= 0)
            {
                return t;
            }

            var slot = t.Rect;
            if (size.Width > slot.Width || size.Height > slot.Height)
            {
                notes.Add($"{t.Label} es de tamaño fijo ({size}) y no cabe entera en su sitio ({slot.Width}x{slot.Height}); se deja con su tamaño.");
            }

            int left = slot.Left + Math.Max(0, (slot.Width - size.Width) / 2);
            int top = t.Kind == TargetKind.Maximized ? slot.Top + Math.Max(0, (slot.Height - size.Height) / 2) : slot.Top;
            return t with { Kind = TargetKind.Visible, Rect = PixelRect.FromSize(left, top, size.Width, size.Height) };
        }).ToList();
        return (result, notes);
    }
}
