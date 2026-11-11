using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace FocusWShell.Win32;

/// <summary>Imagen de un icono en píxeles BGRA premultiplicados, de arriba abajo.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// Iconos de apps pedidos al shell (<c>IShellItemImageFactory</c>), como los muestra el Explorador. Sirve
/// para apps empaquetadas por su AUMID (<c>shell:AppsFolder\…</c>), que si no saldrían con el icono de
/// <c>ApplicationFrameHost</c>, y para ejecutables a los que no se puede leer el icono directamente.
/// </summary>
public static class ShellIcon
{
    /// <summary>Icono de una app empaquetada por su AUMID, o <c>null</c>.</summary>
    public static IconPixels? ForAumid(string aumid, int size) =>
        string.IsNullOrWhiteSpace(aumid) ? null : For(@"shell:AppsFolder\" + aumid, size);

    /// <summary>Icono de un elemento del shell (ruta o <c>shell:</c>), o <c>null</c> si no se pudo.</summary>
    public static unsafe IconPixels? For(string parsingName, int size)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        HBITMAP bitmap = default;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            HRESULT hr;
            object? item;
            fixed (char* name = parsingName)
            {
                hr = PInvoke.SHCreateItemFromParsingName(name, null, &iid, out item);
            }

            if (hr.Failed || item is not IShellItemImageFactory factory)
            {
                return null;
            }

            factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY, &bitmap);
            return bitmap.IsNull ? null : Pixels(bitmap, size);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject(bitmap);
            }
        }
    }

    /// <summary>Copia el bitmap a 32 bits, de arriba abajo (alto negativo), para no perder la transparencia.</summary>
    private static unsafe IconPixels? Pixels(HBITMAP bitmap, int size)
    {
        var dc = PInvoke.CreateCompatibleDC(default);
        try
        {
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = size,
                    biHeight = -size,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = (uint)BI_COMPRESSION.BI_RGB,
                },
            };
            var bgra = new byte[size * size * 4];
            fixed (byte* p = bgra)
            {
                int lines = PInvoke.GetDIBits(dc, bitmap, 0, (uint)size, p, &info, DIB_USAGE.DIB_RGB_COLORS);
                return lines == size ? new IconPixels(size, size, bgra) : null;
            }
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }
}
