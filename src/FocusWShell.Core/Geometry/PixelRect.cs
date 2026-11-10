namespace FocusWShell.Core.Geometry;

/// <summary>
/// Rectángulo en píxeles físicos del escritorio virtual (izquierda/arriba inclusivos,
/// derecha/abajo exclusivos, igual que RECT de Win32).
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    public bool Contains(PixelRect other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>Si el punto está dentro (borde derecho e inferior excluidos).</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var result = new PixelRect(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));
        return result.IsEmpty ? default : result;
    }

    public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
}
