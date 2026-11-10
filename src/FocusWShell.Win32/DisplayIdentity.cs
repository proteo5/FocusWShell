using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace FocusWShell.Win32;

/// <summary>
/// Identidad estable de los monitores activos (S6): de cada ruta de <c>QueryDisplayConfig</c> se
/// obtiene el nombre GDI de su origen (<c>\.\DISPLAYn</c>, para cruzarlo con <c>EnumDisplayMonitors</c>)
/// y la ruta de dispositivo y el nombre de su destino (el monitor físico).
/// </summary>
public static class DisplayIdentity
{
    public sealed record Identity(string GdiName, string DevicePath, string FriendlyName);

    public static unsafe IReadOnlyList<Identity> Query()
    {
        const QUERY_DISPLAY_CONFIG_FLAGS Flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PInvoke.GetDisplayConfigBufferSizes(Flags, out uint pathCount, out uint modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            {
                return [];
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            WIN32_ERROR result;
            fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
            {
                result = PInvoke.QueryDisplayConfig(Flags, &pathCount, p, &modeCount, m, null);
            }

            // Si la configuración cambió entre las dos llamadas, se vuelve a pedir el tamaño.
            if (result == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }

            if (result != WIN32_ERROR.ERROR_SUCCESS)
            {
                return [];
            }

            var identities = new List<Identity>();
            foreach (var path in paths.Take((int)pathCount))
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
                source.header.adapterId = path.sourceInfo.adapterId;
                source.header.id = path.sourceInfo.id;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
                target.header.adapterId = path.targetInfo.adapterId;
                target.header.id = path.targetInfo.id;

                if (PInvoke.DisplayConfigGetDeviceInfo(&source.header) != 0 ||
                    PInvoke.DisplayConfigGetDeviceInfo(&target.header) != 0)
                {
                    continue;
                }

                identities.Add(new Identity(
                    source.viewGdiDeviceName.ToString(),
                    target.monitorDevicePath.ToString(),
                    target.monitorFriendlyDeviceName.ToString()));
            }

            return identities;
        }

        return [];
    }
}
