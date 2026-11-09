namespace FocusWShell.Core.Geometry;

/// <summary>Tamaño en píxeles físicos.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}
