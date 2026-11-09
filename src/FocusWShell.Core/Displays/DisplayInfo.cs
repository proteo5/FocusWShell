using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Displays;

/// <summary>Un monitor tal como lo ve Windows en este momento.</summary>
/// <param name="DeviceName">Nombre GDI (<c>\\.\DISPLAY1</c>). No es estable entre reconexiones; sólo sirve para diagnóstico.</param>
/// <param name="Bounds">Rectángulo completo del monitor.</param>
/// <param name="WorkArea">Área de trabajo: sin la barra de tareas.</param>
public sealed record DisplayInfo(
    nint Handle,
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary,
    int Dpi)
{
    public double Scale => Dpi / 96.0;
}
