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

/// <summary>Resultado de colocar un lote de ventanas.</summary>
/// <param name="Results">Un resultado por ventana, en el orden pedido.</param>
/// <param name="Batched">Si el lote <c>DeferWindowPos</c> se aplicó; si no, se movió ventana por ventana.</param>
/// <param name="Normalized">Ventanas que hubo que sacar de maximizada o minimizada antes del lote.</param>
/// <param name="Corrections">Ventanas que hubo que recolocar individualmente tras el lote.</param>
/// <param name="MoveTime">Tiempo desde el primer cambio visible hasta el fin del lote.</param>
/// <param name="TotalTime">Tiempo total, incluidas verificación y correcciones.</param>
/// <param name="SettleTimes">En modo asíncrono, cuándo llegó cada ventana a su sitio (desde el primer cambio).</param>
public sealed record BatchPlacement(
    IReadOnlyList<PlacementResult> Results,
    bool Batched,
    int Normalized,
    int Corrections,
    TimeSpan MoveTime,
    TimeSpan TotalTime,
    IReadOnlyList<TimeSpan> SettleTimes);

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

    /// <summary>
    /// Coloca varias ventanas como un único cambio: las saca de maximizada/minimizada sin activarlas,
    /// mide los bordes de todas y las mueve en un solo lote <c>DeferWindowPos</c>. Si el lote falla,
    /// reaplica ventana por ventana. Después verifica y corrige individualmente las que la app ajustó.
    /// Con <paramref name="async"/>, en lugar del lote usa <c>SWP_ASYNCWINDOWPOS</c> para que todas
    /// las apps se muevan en paralelo, y espera a que lleguen.
    /// </summary>
    public static unsafe BatchPlacement PlaceBatch(IReadOnlyList<(nint Handle, PixelRect Target)> items, bool async = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        var results = new PlacementResult?[items.Count];
        var live = new List<int>();
        for (int i = 0; i < items.Count; i++)
        {
            var hwnd = new HWND((void*)items[i].Handle);
            if (!PInvoke.IsWindow(hwnd))
            {
                results[i] = new PlacementResult(items[i].Target, default, default, 0, "la ventana ya no existe");
            }
            else if (PInvoke.IsHungAppWindow(hwnd))
            {
                // Una ventana colgada bloquearía EndDeferWindowPos entero.
                results[i] = new PlacementResult(items[i].Target, default, default, 0, "la aplicación no responde");
            }
            else
            {
                live.Add(i);
            }
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        int normalized = 0;
        foreach (int i in live)
        {
            var hwnd = new HWND((void*)items[i].Handle);
            if (PInvoke.IsIconic(hwnd) || PInvoke.IsZoomed(hwnd))
            {
                PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                normalized++;
            }
        }

        var margins = new FrameMargins[items.Count];
        var requests = new PixelRect[items.Count];
        foreach (int i in live)
        {
            var (bounds, visible) = ReadRects(new HWND((void*)items[i].Handle));
            margins[i] = FrameMargins.Between(bounds, visible);
            requests[i] = margins[i].Expand(items[i].Target);
        }

        var settle = new TimeSpan[items.Count];
        bool batched;
        if (async)
        {
            // Cada app recibe la petición en su cola y se mueve en paralelo con las demás.
            foreach (int i in live)
            {
                var r = requests[i];
                PInvoke.SetWindowPos(
                    new HWND((void*)items[i].Handle), HWND.Null, r.Left, r.Top, r.Width, r.Height,
                    MoveFlags | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
            }

            // Espera hasta que todas lleguen (o se detengan en otro tamaño, p. ej. por su mínimo).
            var pending = new HashSet<int>(live);
            var last = live.ToDictionary(i => i, _ => default(PixelRect));
            var stableSince = live.ToDictionary(i => i, _ => TimeSpan.Zero);
            var deadline = clock.Elapsed + TimeSpan.FromMilliseconds(1000);
            while (pending.Count > 0 && clock.Elapsed < deadline)
            {
                foreach (int i in pending.ToList())
                {
                    var (bounds, _) = ReadRects(new HWND((void*)items[i].Handle));
                    if (bounds == requests[i])
                    {
                        settle[i] = clock.Elapsed;
                        pending.Remove(i);
                    }
                    else if (bounds != last[i])
                    {
                        last[i] = bounds;
                        stableSince[i] = clock.Elapsed;
                    }
                    else if (clock.Elapsed - stableSince[i] > TimeSpan.FromMilliseconds(150) && bounds != default)
                    {
                        // Lleva 150 ms quieta en otro rectángulo: la app impuso el suyo.
                        settle[i] = stableSince[i];
                        pending.Remove(i);
                    }
                }

                Thread.Sleep(1);
            }

            batched = true;
        }
        else
        {
            batched = TryDefer(items, live, requests);
        }

        if (!batched)
        {
            foreach (int i in live)
            {
                var r = requests[i];
                PInvoke.SetWindowPos(new HWND((void*)items[i].Handle), HWND.Null, r.Left, r.Top, r.Width, r.Height, MoveFlags);
            }
        }

        var moved = clock.Elapsed;

        // Verificación: las que la app ajustó (bordes que cambian al moverse, DPI) se corrigen una a una.
        int corrections = 0;
        foreach (int i in live)
        {
            var (_, actual) = ReadRects(new HWND((void*)items[i].Handle));
            var result = new PlacementResult(items[i].Target, actual, margins[i], 1, null);
            if (!result.IsExact)
            {
                corrections++;
                var retry = Place(items[i].Handle, items[i].Target);
                result = retry with { Attempts = retry.Attempts + 1 };
            }

            results[i] = result;
        }

        return new BatchPlacement(results.Select(r => r!).ToList(), batched, normalized, corrections, moved, clock.Elapsed, settle);
    }

    private static unsafe bool TryDefer(IReadOnlyList<(nint Handle, PixelRect Target)> items, List<int> live, PixelRect[] requests)
    {
        if (live.Count == 0)
        {
            return true;
        }

        var hdwp = PInvoke.BeginDeferWindowPos(live.Count);
        if (hdwp.IsNull)
        {
            return false;
        }

        foreach (int i in live)
        {
            var r = requests[i];
            hdwp = PInvoke.DeferWindowPos(hdwp, new HWND((void*)items[i].Handle), HWND.Null, r.Left, r.Top, r.Width, r.Height, MoveFlags);
            if (hdwp.IsNull)
            {
                // DeferWindowPos ya liberó el lote al fallar.
                return false;
            }
        }

        return PInvoke.EndDeferWindowPos(hdwp);
    }

    /// <summary>Rectángulo visible actual (sin bordes invisibles).</summary>
    public static unsafe PixelRect VisibleBounds(nint handle) => ReadRects(new HWND((void*)handle)).Visible;

    /// <summary>Si la ventana está minimizada o maximizada.</summary>
    public static unsafe bool IsMinimizedOrMaximized(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        return PInvoke.IsIconic(hwnd) || PInvoke.IsZoomed(hwnd);
    }

    /// <summary>Da el foco a la ventana. Sólo funciona justo después de input del usuario al gestor (atajo o clic).</summary>
    public static unsafe bool Activate(nint handle) => PInvoke.SetForegroundWindow(new HWND((void*)handle));

    /// <summary>Ventana en primer plano.</summary>
    public static nint Foreground() => PInvoke.GetForegroundWindow();

    /// <summary>Minimiza sin activar (ventanas ocultas de una pila, ADR 0006).</summary>
    public static unsafe string? Minimize(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd))
        {
            return "la ventana ya no existe";
        }

        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return "la aplicación no responde";
        }

        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE);
        return null;
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
