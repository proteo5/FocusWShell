using FocusWShell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>Ventanas propias del gestor (tiras de pestañas): sin foco, sin barra de tareas, en píxeles físicos.</summary>
public static class OwnWindows
{
    /// <summary>
    /// <c>WS_EX_TOOLWINDOW</c> (fuera de la barra de tareas y de Alt+Tab) y <c>WS_EX_NOACTIVATE</c>
    /// (hacer clic no le quita el foco a la app que lo tiene).
    /// </summary>
    public static unsafe void MakeToolNoActivate(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        int ex = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= (int)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE);
        ex &= ~(int)WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        _ = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// La coloca exactamente en <paramref name="rect"/> (píxeles físicos) y la muestra sin activarla.
    /// Dos pasadas: si cruza a otro DPI, el reescalado de WPF cambiaría el tamaño en la primera.
    /// </summary>
    public static unsafe void Place(nint handle, PixelRect rect)
    {
        var hwnd = new HWND((void*)handle);
        const SET_WINDOW_POS_FLAGS Flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
        for (int i = 0; i < 2; i++)
        {
            PInvoke.SetWindowPos(hwnd, HWND.Null, // HWND_TOP = 0
                rect.Left, rect.Top, rect.Width, rect.Height, Flags);
        }
    }
}
