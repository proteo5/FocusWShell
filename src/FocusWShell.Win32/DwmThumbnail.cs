using FocusWShell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace FocusWShell.Win32;

/// <summary>
/// Miniatura en vivo de una ventana dibujada por DWM dentro de una ventana propia (ADR 0015, S10). Sigue
/// en vivo aunque la fuente esté tapada; si está minimizada, muestra su última imagen.
/// </summary>
public sealed class DwmThumbnail : IDisposable
{
    // DWM_TNP_*: qué campos de DWM_THUMBNAIL_PROPERTIES se aplican.
    private const uint TnpRectDestination = 0x1;
    private const uint TnpOpacity = 0x4;
    private const uint TnpVisible = 0x8;
    private const uint TnpSourceClientAreaOnly = 0x10;

    private nint _thumb;

    private DwmThumbnail(nint thumb, nint source)
    {
        _thumb = thumb;
        Source = source;
    }

    public nint Source { get; }

    /// <summary>Registra la miniatura de <paramref name="source"/> en <paramref name="host"/>, o <c>null</c> si DWM no la acepta.</summary>
    public static unsafe DwmThumbnail? Register(nint host, nint source)
    {
        var hr = PInvoke.DwmRegisterThumbnail(new HWND((void*)host), new HWND((void*)source), out var thumb);
        return hr.Succeeded ? new DwmThumbnail(thumb, source) : null;
    }

    /// <summary>Dónde se dibuja, en píxeles físicos relativos al área de la ventana anfitriona.</summary>
    public void Show(PixelRect destination, bool visible = true)
    {
        if (_thumb == 0)
        {
            return;
        }

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = TnpRectDestination | TnpOpacity | TnpVisible | TnpSourceClientAreaOnly,
            rcDestination = new RECT(destination.Left, destination.Top, destination.Right, destination.Bottom),
            opacity = 255,
            fVisible = visible,
            fSourceClientAreaOnly = true,
        };
        _ = PInvoke.DwmUpdateThumbnailProperties(_thumb, in props);
    }

    public void Dispose()
    {
        if (_thumb != 0)
        {
            _ = PInvoke.DwmUnregisterThumbnail(_thumb);
            _thumb = 0;
        }
    }
}
