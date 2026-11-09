using System.Runtime.InteropServices;
using FocusWShell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>Resultado de colocar una ventana.</summary>
/// <param name="Target">Rectángulo visible pedido.</param>
/// <param name="Actual">Rectángulo visible obtenido.</param>
/// <param name="Margins">Bordes invisibles medidos en el último intento.</param>
/// <param name="Attempts">Intentos de <c>SetWindowPos</c> realizados.</param>
/// <param name="Error">Descripción del fallo, o <c>null</c>.</param>
public sealed record PlacementResult(
    PixelRect Target,
    PixelRect Actual,
    FrameMargins Margins,
    int Attempts,
    string? Error)
{
    /// <summary>Desviación máxima de un borde, en píxeles.</summary>
    public int MaxDeviation => Math.Max(
        Math.Max(Math.Abs(Actual.Left - Target.Left), Math.Abs(Actual.Top - Target.Top)),
        Math.Max(Math.Abs(Actual.Right - Target.Right), Math.Abs(Actual.Bottom - Target.Bottom)));

    public bool IsExact => Error is null && MaxDeviation <= 1;
}

/// <summary>Mueve, captura y restaura ventanas de otros procesos.</summary>
public static class WindowMover
{
    private const SET_WINDOW_POS_FLAGS MoveFlags =
        SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;

    /// <summary>Si el handle sigue siendo una ventana viva del mismo proceso.</summary>
    public static unsafe bool IsSameWindow(long handle, int processId)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd))
        {
            return false;
        }

        uint pid;
        return PInvoke.GetWindowThreadProcessId(hwnd, &pid) != 0 && pid == processId;
    }

    public static unsafe SavedPlacement? Capture(nint handle, int processId, string processName, string title)
    {
        var hwnd = new HWND((void*)handle);
        var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!PInvoke.GetWindowPlacement(hwnd, ref wp))
        {
            return null;
        }

        var r = wp.rcNormalPosition;
        return new SavedPlacement(
            handle, processId, processName, title,
            (uint)wp.flags, (uint)wp.showCmd,
            wp.ptMinPosition.X, wp.ptMinPosition.Y, wp.ptMaxPosition.X, wp.ptMaxPosition.Y,
            new PixelRect(r.left, r.top, r.right, r.bottom));
    }

    /// <summary>Devuelve la ventana a la colocación guardada. No hace nada si el handle ya no es la misma ventana.</summary>
    public static unsafe string? Restore(SavedPlacement saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (!IsSameWindow(saved.Handle, saved.ProcessId))
        {
            return "la ventana ya no existe";
        }

        var hwnd = new HWND((void*)saved.Handle);
        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return "la aplicación no responde";
        }

        var n = saved.NormalPosition;
        var wp = new WINDOWPLACEMENT
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            flags = (WINDOWPLACEMENT_FLAGS)saved.Flags,
            showCmd = RestoreShowCmd((SHOW_WINDOW_CMD)saved.ShowCmd),
            ptMinPosition = new System.Drawing.Point(saved.MinX, saved.MinY),
            ptMaxPosition = new System.Drawing.Point(saved.MaxX, saved.MaxY),
            rcNormalPosition = new RECT(n.Left, n.Top, n.Right, n.Bottom),
        };

        return PInvoke.SetWindowPlacement(hwnd, in wp) ? null : $"SetWindowPlacement falló ({Marshal.GetLastPInvokeError()})";
    }

    /// <summary>
    /// Coloca la ventana para que su parte visible ocupe <paramref name="visibleTarget"/>.
    /// La saca de maximizada/minimizada sin activarla, compensa los bordes invisibles
    /// y verifica el resultado; reintenta una vez si la app se ajustó por su cuenta.
    /// </summary>
    public static unsafe PlacementResult Place(nint handle, PixelRect visibleTarget)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd))
        {
            return new PlacementResult(visibleTarget, default, default, 0, "la ventana ya no existe");
        }

        // SetWindowPos sobre una ventana colgada bloquearía este hilo.
        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return new PlacementResult(visibleTarget, default, default, 0, "la aplicación no responde");
        }

        if (PInvoke.IsIconic(hwnd) || PInvoke.IsZoomed(hwnd))
        {
            // Vuelve al estado normal sin activar. Los bordes de una ventana maximizada son distintos,
            // así que los márgenes se miden después.
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }

        FrameMargins margins = default;
        PixelRect actual = default;
        const int MaxAttempts = 2;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var (bounds, visible) = ReadRects(hwnd);
            margins = FrameMargins.Between(bounds, visible);
            var request = margins.Expand(visibleTarget);

            if (!PInvoke.SetWindowPos(hwnd, HWND.Null, request.Left, request.Top, request.Width, request.Height, MoveFlags))
            {
                var (_, current) = ReadRects(hwnd);
                return new PlacementResult(
                    visibleTarget, current, margins, attempt, $"SetWindowPos falló ({Marshal.GetLastPInvokeError()})");
            }

            (_, actual) = ReadRects(hwnd);
            var result = new PlacementResult(visibleTarget, actual, margins, attempt, null);
            if (result.IsExact)
            {
                return result;
            }
        }

        return new PlacementResult(visibleTarget, actual, margins, MaxAttempts, null);
    }

    private static unsafe (PixelRect Bounds, PixelRect Visible) ReadRects(HWND hwnd)
    {
        PInvoke.GetWindowRect(hwnd, out var r);
        var bounds = new PixelRect(r.left, r.top, r.right, r.bottom);

        RECT v;
        var hr = PInvoke.DwmGetWindowAttribute(
            hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &v, (uint)sizeof(RECT));
        var visible = hr.Succeeded ? new PixelRect(v.left, v.top, v.right, v.bottom) : bounds;
        return (bounds, visible);
    }

    /// <summary>Al restaurar no se activa la ventana salvo que estuviera maximizada (no hay variante sin activar).</summary>
    private static SHOW_WINDOW_CMD RestoreShowCmd(SHOW_WINDOW_CMD captured) => captured switch
    {
        SHOW_WINDOW_CMD.SW_SHOWMINIMIZED => SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE,
        SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED => SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED,
        _ => SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE,
    };
}
