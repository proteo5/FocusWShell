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
            return Strings.Move_Gone;
        }

        var hwnd = new HWND((void*)saved.Handle);
        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return Strings.Move_Hung;
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

        return PInvoke.SetWindowPlacement(hwnd, in wp) ? null : Text.F(Strings.Move_ApiFailed, "SetWindowPlacement", Marshal.GetLastPInvokeError());
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
            return new PlacementResult(visibleTarget, default, default, 0, Strings.Move_Gone);
        }

        // SetWindowPos sobre una ventana colgada bloquearía este hilo.
        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return new PlacementResult(visibleTarget, default, default, 0, Strings.Move_Hung);
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
                    visibleTarget, current, margins, attempt, Text.F(Strings.Move_ApiFailed, "SetWindowPos", Marshal.GetLastPInvokeError()));
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
                results[i] = new PlacementResult(items[i].Target, default, default, 0, Strings.Move_Gone);
            }
            else if (PInvoke.IsHungAppWindow(hwnd))
            {
                // Una ventana colgada bloquearía EndDeferWindowPos entero.
                results[i] = new PlacementResult(items[i].Target, default, default, 0, Strings.Move_Hung);
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

        // Ventanas que cambian a un monitor con otro DPI: la app se reescala por su cuenta al llegar
        // (WM_DPICHANGED), después de que el movimiento asíncrono ya se diera por bueno.
        var crossesDpi = live.Where(i =>
            PInvoke.GetDpiForWindow(new HWND((void*)items[i].Handle)) != DpiAt(items[i].Target)).ToList();
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

        if (crossesDpi.Count > 0)
        {
            WaitUntilQuiet(items, crossesDpi, clock);
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

    /// <summary>DPI del monitor donde cae el rectángulo.</summary>
    private static unsafe uint DpiAt(PixelRect rect)
    {
        var r = new RECT(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var monitor = PInvoke.MonitorFromRect(&r, Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return PInvoke.GetDpiForMonitor(monitor, Windows.Win32.UI.HiDpi.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Succeeded
            ? dpi
            : 96;
    }

    /// <summary>Espera a que las ventanas lleven 100 ms sin cambiar de rectángulo (máximo 800 ms).</summary>
    private static unsafe void WaitUntilQuiet(
        IReadOnlyList<(nint Handle, PixelRect Target)> items, List<int> which, System.Diagnostics.Stopwatch clock)
    {
        var quiet = TimeSpan.FromMilliseconds(100);
        var deadline = clock.Elapsed + TimeSpan.FromMilliseconds(800);
        var last = which.ToDictionary(i => i, i => ReadRects(new HWND((void*)items[i].Handle)).Bounds);
        var changedAt = which.ToDictionary(i => i, _ => clock.Elapsed);
        while (clock.Elapsed < deadline && which.Any(i => clock.Elapsed - changedAt[i] < quiet))
        {
            Thread.Sleep(5);
            foreach (int i in which)
            {
                var now = ReadRects(new HWND((void*)items[i].Handle)).Bounds;
                if (now != last[i])
                {
                    last[i] = now;
                    changedAt[i] = clock.Elapsed;
                }
            }
        }
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
    /// <summary>
    /// Pone la ventana en primer plano. Windows sólo lo permite a quien recibió la última entrada; desde el
    /// deck (que no se activa, S8) y con el toque de una tablet por Apollo a veces no cuenta como nuestra,
    /// y la ventana se queda detrás. Entonces se engancha un momento a la cola de entrada del hilo que
    /// tiene el primer plano, que sí puede cederlo.
    /// </summary>
    public static unsafe bool Activate(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        if (PInvoke.SetForegroundWindow(hwnd) && PInvoke.GetForegroundWindow() == hwnd)
        {
            return true;
        }

        var foreground = PInvoke.GetForegroundWindow();
        uint foregroundThread = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground, null);
        uint me = PInvoke.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != me && PInvoke.AttachThreadInput(me, foregroundThread, true);
        try
        {
            PInvoke.BringWindowToTop(hwnd);
            PInvoke.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(me, foregroundThread, false);
            }
        }

        return PInvoke.GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// Manda la ventana al fondo del orden Z sin moverla ni activarla. Las ventanas que viven detrás de
    /// una lista (ADR 0015) no tienen por qué tapar nada: se ven por sus miniaturas. Si el usuario la
    /// activa, Windows la vuelve a traer al frente.
    /// </summary>
    public static unsafe void SendToBack(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        if (PInvoke.IsWindow(hwnd))
        {
            PInvoke.SetWindowPos(hwnd, new HWND((void*)1), // HWND_BOTTOM
                0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
        }
    }

    /// <summary>
    /// Cierra la ventana como su botón X (<c>WM_SYSCOMMAND</c> + <c>SC_CLOSE</c>): la app decide, y si tiene
    /// algo sin guardar, pregunta. No espera a que se cierre.
    /// </summary>
    public static unsafe bool Close(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        return PInvoke.IsWindow(hwnd) && PInvoke.PostMessage(hwnd, 0x0112 /* WM_SYSCOMMAND */, new WPARAM(0xF060 /* SC_CLOSE */), default);
    }

    /// <summary>Lleva el cursor al centro de la ventana (al elegirla en el deck, para seguir trabajando en ella).</summary>
    public static unsafe void PointAt(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd) || PInvoke.IsIconic(hwnd))
        {
            return;
        }

        var visible = ReadRects(hwnd).Visible;
        if (!visible.IsEmpty)
        {
            PInvoke.SetCursorPos(visible.Left + (visible.Width / 2), visible.Top + (visible.Height / 2));
        }
    }

    /// <summary>Estado actual de la ventana, para compararlo con lo que el gestor espera (S4).</summary>
    public static unsafe FocusWShell.Core.Domain.ObservedWindow Observe(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd))
        {
            return new(handle, false, false, false, default);
        }

        bool minimized = PInvoke.IsIconic(hwnd);
        return new(handle, true, minimized, PInvoke.IsZoomed(hwnd), minimized ? default : ReadRects(hwnd).Visible);
    }

    /// <summary>Ventana en primer plano.</summary>
    public static nint Foreground() => PInvoke.GetForegroundWindow();

    /// <summary>Si la ventana ya está maximizada en el monitor de <paramref name="workArea"/>.</summary>
    public static unsafe bool IsMaximizedOn(nint handle, PixelRect workArea)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd) || !PInvoke.IsZoomed(hwnd))
        {
            return false;
        }

        var visible = ReadRects(hwnd).Visible;
        return !visible.Intersect(workArea).IsEmpty && workArea.Contains(visible.Left + (visible.Width / 2), visible.Top + (visible.Height / 2));
    }

    /// <summary>
    /// Maximiza la ventana en el monitor de <paramref name="workArea"/> (template <c>Fill</c>, ADR 0008).
    /// Si está maximizada en otro monitor, primero la lleva ahí. Windows no tiene un "maximizar sin
    /// activar": si al maximizar se lleva el foco, se intenta devolvérselo a la ventana que lo tenía.
    /// </summary>
    public static unsafe PlacementResult MaximizeOn(nint handle, PixelRect workArea)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd))
        {
            return new PlacementResult(workArea, default, default, 0, Strings.Move_Gone);
        }

        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return new PlacementResult(workArea, default, default, 0, Strings.Move_Hung);
        }

        if (!IsMaximizedOn(handle, workArea))
        {
            if (PInvoke.IsZoomed(hwnd) || PInvoke.IsIconic(hwnd) || !workArea.Contains(ReadRects(hwnd).Visible.Left + 1, ReadRects(hwnd).Visible.Top + 1))
            {
                // Maximizada en otro monitor, minimizada o fuera: primero a este monitor, en normal.
                Place(handle, workArea);
            }

            var previous = PInvoke.GetForegroundWindow();
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED);
            if (previous != hwnd && !previous.IsNull && PInvoke.GetForegroundWindow() == hwnd)
            {
                PInvoke.SetForegroundWindow(previous);
            }
        }

        // Maximizar se anima: se espera a que el rectángulo deje de cambiar antes de medir.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var actual = ReadRects(hwnd).Visible;
        var changedAt = TimeSpan.Zero;
        while (clock.Elapsed - changedAt < TimeSpan.FromMilliseconds(60) && clock.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            Thread.Sleep(5);
            var now = ReadRects(hwnd).Visible;
            if (now != actual)
            {
                actual = now;
                changedAt = clock.Elapsed;
            }
        }

        return new PlacementResult(workArea, actual, default, 1, null);
    }

    /// <summary>Muestra una ventana minimizada sin activarla.</summary>
    public static unsafe void ShowNoActivate(nint handle) =>
        PInvoke.ShowWindow(new HWND((void*)handle), SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);

    /// <summary>Maximiza la ventana (activándola: no hay variante sin activar). Sólo para pruebas.</summary>
    public static unsafe void Maximize(nint handle) =>
        PInvoke.ShowWindow(new HWND((void*)handle), SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED);

    /// <summary>Minimiza sin activar (ventanas ocultas de una pila, ADR 0006).</summary>
    public static unsafe string? Minimize(nint handle)
    {
        var hwnd = new HWND((void*)handle);
        if (!PInvoke.IsWindow(hwnd))
        {
            return Strings.Move_Gone;
        }

        if (PInvoke.IsHungAppWindow(hwnd))
        {
            return Strings.Move_Hung;
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
