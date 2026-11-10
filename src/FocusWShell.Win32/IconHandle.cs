using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

public static class IconHandle
{
    /// <summary>Libera un HICON creado con <c>Bitmap.GetHicon</c>.</summary>
    public static unsafe bool Destroy(nint handle) => PInvoke.DestroyIcon(new HICON((void*)handle));
}
