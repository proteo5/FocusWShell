using System.Runtime.InteropServices;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>Enumera las ventanas top-level y toma una <see cref="WindowSnapshot"/> de cada una.</summary>
public static class WindowEnumerator
{
    /// <summary>Todas las ventanas top-level, en orden Z (de arriba abajo).</summary>
    public static IReadOnlyList<WindowSnapshot> EnumerateTopLevel()
    {
        var handles = new List<HWND>();
        PInvoke.EnumWindows(
            (hwnd, _) =>
            {
                handles.Add(hwnd);
                return true;
            },
            default);

        int ownPid = Environment.ProcessId;
        var processCache = new Dictionary<uint, (string Path, IntegrityLevel Integrity)>();
        var result = new List<WindowSnapshot>(handles.Count);
        foreach (var hwnd in handles)
        {
            var snapshot = TrySnapshot(hwnd, ownPid, processCache);
            if (snapshot is not null)
            {
                result.Add(snapshot);
            }
        }

        return result;
    }

    /// <summary>Nivel de integridad del proceso actual.</summary>
    public static IntegrityLevel CurrentIntegrity() => ProcessInfo.CurrentIntegrity();

    private static unsafe WindowSnapshot? TrySnapshot(
        HWND hwnd, int ownPid, Dictionary<uint, (string Path, IntegrityLevel Integrity)> processCache)
    {
        uint pid;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0)
        {
            return null; // la ventana ya no existe
        }

        if (!processCache.TryGetValue(pid, out var process))
        {
            process = ProcessInfo.Query(pid);
            processCache[pid] = process;
        }

        var style = (WINDOW_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var exStyle = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        PInvoke.GetWindowRect(hwnd, out var rect);
        var bounds = ToPixelRect(rect);

        return new WindowSnapshot
        {
            Handle = (nint)hwnd.Value,
            Title = GetTitle(hwnd),
            ClassName = GetClass(hwnd),
            ProcessId = (int)pid,
            ProcessPath = process.Path,
            ProcessName = Path.GetFileName(process.Path),
            AppUserModelId = GetAppUserModelId(hwnd),
            IsVisible = PInvoke.IsWindowVisible(hwnd),
            IsCloaked = IsCloaked(hwnd),
            IsMinimized = PInvoke.IsIconic(hwnd),
            IsMaximized = PInvoke.IsZoomed(hwnd),
            HasOwner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER) != HWND.Null,
            IsToolWindow = exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW),
            IsAppWindow = exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_APPWINDOW),
            IsNoActivate = exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_NOACTIVATE),
            IsTopmost = exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TOPMOST),
            HasCaption = (style & WINDOW_STYLE.WS_CAPTION) == WINDOW_STYLE.WS_CAPTION,
            IsResizable = style.HasFlag(WINDOW_STYLE.WS_THICKFRAME),
            Bounds = bounds,
            VisibleBounds = GetVisibleBounds(hwnd) ?? bounds,
            MonitorBounds = GetMonitorBounds(hwnd),
            Dpi = (int)PInvoke.GetDpiForWindow(hwnd),
            Integrity = process.Integrity,
            IsOwnProcess = pid == ownPid,
        };
    }

    private static unsafe string GetTitle(HWND hwnd)
    {
        int length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        Span<char> buffer = length < 512 ? stackalloc char[length + 1] : new char[length + 1];
        fixed (char* p = buffer)
        {
            int copied = PInvoke.GetWindowText(hwnd, p, buffer.Length);
            return new string(p, 0, copied);
        }
    }

    private static unsafe string GetClass(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* p = buffer)
        {
            int copied = PInvoke.GetClassName(hwnd, p, buffer.Length);
            return new string(p, 0, copied);
        }
    }

    private static unsafe bool IsCloaked(HWND hwnd)
    {
        uint cloaked = 0;
        var hr = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint));
        return hr.Succeeded && cloaked != 0;
    }

    private static unsafe PixelRect? GetVisibleBounds(HWND hwnd)
    {
        RECT rect;
        var hr = PInvoke.DwmGetWindowAttribute(
            hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT));
        return hr.Succeeded ? ToPixelRect(rect) : null;
    }

    private static PixelRect GetMonitorBounds(HWND hwnd)
    {
        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        return PInvoke.GetMonitorInfo(monitor, ref info) ? ToPixelRect(info.rcMonitor) : default;
    }

    private static unsafe string? GetAppUserModelId(HWND hwnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            PInvoke.SHGetPropertyStoreForWindow(hwnd, &iid, out object storeObject).ThrowOnFailure();
            var store = (IPropertyStore)storeObject;
            try
            {
                var key = PInvoke.PKEY_AppUserModel_ID;
                store.GetValue(key, out var value);
                try
                {
                    return PropVariantToString(value);
                }
                finally
                {
                    PInvoke.PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static unsafe string? PropVariantToString(in Windows.Win32.System.Com.StructuredStorage.PROPVARIANT value)
    {
        const ushort VT_LPWSTR = 31;
        return (ushort)value.Anonymous.Anonymous.vt == VT_LPWSTR
            ? new string(value.Anonymous.Anonymous.Anonymous.pwszVal.Value)
            : null;
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.left, r.top, r.right, r.bottom);
}
