using FocusWShell.Core.Displays;
using FocusWShell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace FocusWShell.Win32;

/// <summary>Enumera los monitores conectados.</summary>
public static class DisplayEnumerator
{
    public static unsafe IReadOnlyList<DisplayInfo> Enumerate()
    {
        var monitors = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(
            HDC.Null,
            (RECT?)null,
            (monitor, _, _, _) =>
            {
                monitors.Add(monitor);
                return true;
            },
            default);

        // Identidad estable (S6): se cruza por el nombre GDI.
        var identities = DisplayIdentity.Query().ToDictionary(i => i.GdiName, StringComparer.OrdinalIgnoreCase);
        var result = new List<DisplayInfo>(monitors.Count);
        foreach (var monitor in monitors)
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                continue;
            }

            uint dpi = 96;
            if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded)
            {
                dpi = dpiX;
            }

            const uint MONITORINFOF_PRIMARY = 1;
            string gdi = info.szDevice.ToString();
            identities.TryGetValue(gdi, out var identity);
            result.Add(new DisplayInfo(
                (nint)monitor.Value,
                gdi,
                ToPixelRect(info.monitorInfo.rcMonitor),
                ToPixelRect(info.monitorInfo.rcWork),
                (info.monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0,
                (int)dpi)
            {
                StableId = string.IsNullOrEmpty(identity?.DevicePath) ? null : identity.DevicePath,
                FriendlyName = string.IsNullOrEmpty(identity?.FriendlyName) ? null : identity.FriendlyName,
            });
        }

        return result;
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.left, r.top, r.right, r.bottom);
}
