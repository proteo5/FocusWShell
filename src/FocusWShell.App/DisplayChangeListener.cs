using System.Windows.Interop;
using System.Windows.Threading;

namespace FocusWShell.App;

/// <summary>
/// Ventana oculta de nivel superior que recibe los avisos de Windows sobre monitores (S6):
/// <c>WM_DISPLAYCHANGE</c> y el cambio de área de trabajo (<c>WM_SETTINGCHANGE</c> con
/// <c>SPI_SETWORKAREA</c>). Una ventana sólo de mensajes no recibe esos avisos de difusión.
/// Agrupa la ráfaga: avisa al empezar y otra vez cuando lleva <c>settle</c> sin avisos.
/// <para>
/// También la suspensión (M2, <c>WM_POWERBROADCAST</c>): al suspender empieza un cambio que dura hasta
/// la reanudación y unos segundos más, porque al despertar los monitores llegan tarde y a ráfagas, y
/// Windows recoloca ventanas por su cuenta. Al terminar se avisa con <c>resumed = true</c>.
/// </para>
/// </summary>
public sealed class DisplayChangeListener : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int SpiSetWorkArea = 0x002F;
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;

    /// <summary>Espera tras reanudar: los monitores (y una tablet por wifi) tardan en volver.</summary>
    public static readonly TimeSpan ResumeSettle = TimeSpan.FromSeconds(4);

    private readonly HwndSource _source;
    private readonly DispatcherTimer _settle;
    private readonly Action _onStart;
    private readonly Action<bool> _onSettled;
    private readonly TimeSpan _normalSettle;
    private bool _changing;
    private bool _suspended;
    private bool _resumed;

    /// <param name="onSettled">Se llama al estabilizarse; el argumento dice si venía de una suspensión.</param>
    public DisplayChangeListener(Action onStart, Action<bool> onSettled, TimeSpan? settle = null)
    {
        _onStart = onStart;
        _onSettled = onSettled;
        _normalSettle = settle ?? TimeSpan.FromMilliseconds(1500);
        _settle = new DispatcherTimer { Interval = _normalSettle };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            _changing = false;
            bool resumed = _resumed;
            _resumed = false;
            _settle.Interval = _normalSettle;
            _onSettled(resumed);
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
        if (msg == WmPowerBroadcast)
        {
            OnPower(wParam.ToInt32());
            return IntPtr.Zero;
        }

        if (_suspended)
        {
            return IntPtr.Zero; // dormido no hay nada que reconciliar: espera a la reanudación
        }

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

    private void OnPower(int kind)
    {
        switch (kind)
        {
            case PbtApmSuspend:
                _suspended = true;
                _settle.Stop();
                if (!_changing)
                {
                    _changing = true;
                    _onStart();
                }

                break;

            // Llegan las dos (automática siempre; la otra si hay alguien delante). La segunda sólo alarga la espera.
            case PbtApmResumeAutomatic or PbtApmResumeSuspend when _suspended || _resumed:
                _suspended = false;
                _resumed = true;
                _settle.Stop();
                _settle.Interval = ResumeSettle;
                _settle.Start();
                break;
        }
    }

    public void Dispose()
    {
        _settle.Stop();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
