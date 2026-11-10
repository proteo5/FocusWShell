using System.Windows.Interop;
using System.Windows.Threading;

namespace FocusWShell.App;

/// <summary>
/// Ventana oculta de nivel superior que recibe los avisos de Windows sobre monitores (S6):
/// <c>WM_DISPLAYCHANGE</c> y el cambio de área de trabajo (<c>WM_SETTINGCHANGE</c> con
/// <c>SPI_SETWORKAREA</c>). Una ventana sólo de mensajes no recibe esos avisos de difusión.
/// Agrupa la ráfaga: avisa al empezar y otra vez cuando lleva <c>settle</c> sin avisos.
/// </summary>
public sealed class DisplayChangeListener : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;

    private readonly HwndSource _source;
    private readonly DispatcherTimer _settle;
    private readonly Action _onStart;
    private readonly Action _onSettled;
    private bool _changing;

    public DisplayChangeListener(Action onStart, Action onSettled, TimeSpan? settle = null)
    {
        _onStart = onStart;
        _onSettled = onSettled;
        _settle = new DispatcherTimer { Interval = settle ?? TimeSpan.FromMilliseconds(1500) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            _changing = false;
            _onSettled();
        };

        // WS_POPUP sin WS_VISIBLE: de nivel superior pero invisible.
        var parameters = new HwndSourceParameters("FocusWShell · monitores")
        {
            WindowStyle = unchecked((int)0x80000000),
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDisplayChange || (msg == WmSettingChange && wParam.ToInt32() == SpiSetWorkArea))
        {
            if (!_changing)
            {
                _changing = true;
                _onStart();
            }

            // Cada aviso reinicia la espera: las reconexiones llegan en ráfagas.
            _settle.Stop();
            _settle.Start();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _settle.Stop();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
