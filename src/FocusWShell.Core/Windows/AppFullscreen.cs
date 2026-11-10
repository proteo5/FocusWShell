using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Windows;

/// <summary>
/// Pantalla completa propia de una app (M2): F11 en el navegador, un vídeo, una presentación, un juego.
/// Una ventana lo está si cubre su monitor entero (barra de tareas incluida) sin estar maximizada; una
/// ventana maximizada en un monitor sin barra también lo cubre, pero no cuenta.
/// </summary>
public static class AppFullscreen
{
    /// <summary>Holgura por borde, en píxeles.</summary>
    public const int Tolerance = 2;

    public static bool Covers(PixelRect window, PixelRect monitor, bool maximized) =>
        !maximized && !monitor.IsEmpty
        && window.Left <= monitor.Left + Tolerance && window.Top <= monitor.Top + Tolerance
        && window.Right >= monitor.Right - Tolerance && window.Bottom >= monitor.Bottom - Tolerance;
}
