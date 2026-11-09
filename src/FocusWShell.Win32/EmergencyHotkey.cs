using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>
/// Atajo global en un hilo propio con su cola de mensajes (<c>RegisterHotKey</c> sin ventana).
/// Así responde aunque la UI del gestor esté ocupada (03-arquitectura, recuperación ante fallos).
/// El callback se ejecuta en ese hilo.
/// </summary>
public sealed class EmergencyHotkey : IDisposable
{
    /// <summary>Ctrl+Alt+Mayús+Retroceso.</summary>
    public const string Description = "Ctrl+Alt+Mayús+Retroceso";

    private const int HotkeyId = 0xF0C5;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private bool _registered;

    private EmergencyHotkey(Action onPressed)
    {
        _thread = new Thread(() => Run(onPressed)) { IsBackground = true, Name = "FocusWShell emergency hotkey" };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Si Windows aceptó el atajo (falla si otra app ya lo registró).</summary>
    public bool IsRegistered => _registered;

    public static EmergencyHotkey Start(Action onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);
        return new EmergencyHotkey(onPressed);
    }

    private void Run(Action onPressed)
    {
        _threadId = PInvoke.GetCurrentThreadId();
        _registered = PInvoke.RegisterHotKey(
            HWND.Null,
            HotkeyId,
            HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT,
            0x08); // VK_BACK
        _ready.Set();
        if (!_registered)
        {
            return;
        }

        try
        {
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
            {
                if (msg.message == PInvoke.WM_HOTKEY && (int)msg.wParam.Value == HotkeyId)
                {
                    onPressed();
                }
            }
        }
        finally
        {
            PInvoke.UnregisterHotKey(HWND.Null, HotkeyId);
        }
    }

    public void Dispose()
    {
        if (_registered && _thread.IsAlive)
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }
}
