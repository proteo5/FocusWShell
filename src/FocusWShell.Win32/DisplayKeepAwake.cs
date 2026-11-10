using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace FocusWShell.Win32;

/// <summary>
/// Mantiene la pantalla encendida y evita el protector de pantalla mientras esté activo (ADR 0010).
/// Usa una petición de energía con motivo, visible en <c>powercfg /requests</c>; Windows la retira
/// sola si el proceso termina.
/// </summary>
public sealed class DisplayKeepAwake : IDisposable
{
    public const string Reason = "FocusWShell: mantener la pantalla encendida";

    private readonly Lock _gate = new();
    private SafeFileHandle? _request;

    public bool IsActive { get; private set; }

    /// <summary>Activa o desactiva. Devuelve si quedó en el estado pedido.</summary>
    public bool Set(bool active)
    {
        lock (_gate)
        {
            if (active == IsActive)
            {
                return true;
            }

            if (active)
            {
                _request ??= Create();
                if (_request is null || !PInvoke.PowerSetRequest(_request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired))
                {
                    return false;
                }
            }
            else if (_request is not null)
            {
                PInvoke.PowerClearRequest(_request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);
            }

            IsActive = active;
            return true;
        }
    }

    private static unsafe SafeFileHandle? Create()
    {
        nint text = Marshal.StringToHGlobalUni(Reason);
        try
        {
            var context = new REASON_CONTEXT
            {
                Version = PInvoke.POWER_REQUEST_CONTEXT_VERSION,
                Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            };
            context.Reason.SimpleReasonString = new PWSTR((char*)text);
            var handle = PInvoke.PowerCreateRequest(context);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    public void Dispose()
    {
        Set(false);
        _request?.Dispose();
    }
}
