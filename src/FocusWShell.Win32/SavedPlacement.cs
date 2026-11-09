using FocusWShell.Core.Geometry;

namespace FocusWShell.Win32;

/// <summary>
/// Copia serializable de <c>WINDOWPLACEMENT</c> más los datos para reconocer la ventana.
/// Es el <c>OriginalState</c> del modelo (docs/02-modelo-de-dominio.md).
/// </summary>
/// <param name="NormalPosition">Rectángulo "restaurado", en coordenadas de área de trabajo (así lo maneja Windows).</param>
public sealed record SavedPlacement(
    long Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    uint Flags,
    uint ShowCmd,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    PixelRect NormalPosition);
