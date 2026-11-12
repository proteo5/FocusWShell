using System.Runtime.InteropServices;

namespace FocusWShell.Win32;

/// <summary>
/// Escritorios virtuales de Windows con sólo la API documentada (<c>IVirtualDesktopManager</c>), ver
/// ADR 0016. Si el servicio no está disponible, todo responde como si hubiera un solo escritorio.
/// </summary>
public static class VirtualDesktops
{
    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }

    private static readonly Guid ManagerClsid = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    /// <summary>Uno por hilo: el objeto COM no se comparte entre apartamentos.</summary>
    [ThreadStatic]
    private static IVirtualDesktopManager? _manager;

    [ThreadStatic]
    private static bool _unavailable;

    private static IVirtualDesktopManager? Manager()
    {
        if (_manager is null && !_unavailable)
        {
            try
            {
                _manager = Type.GetTypeFromCLSID(ManagerClsid) is { } type ? Activator.CreateInstance(type) as IVirtualDesktopManager : null;
            }
            catch (COMException)
            {
            }

            _unavailable = _manager is null;
        }

        return _manager;
    }

    /// <summary>
    /// Si la ventana está en otro escritorio virtual (Windows la tiene oculta por eso). Falso si está en el
    /// actual, en todos, o si no se pudo saber.
    /// </summary>
    public static bool IsOnOtherDesktop(nint handle)
    {
        try
        {
            return Manager() is { } manager
                && manager.IsWindowOnCurrentVirtualDesktop(handle, out bool onCurrent) >= 0
                && !onCurrent;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return false;
        }
    }
}
