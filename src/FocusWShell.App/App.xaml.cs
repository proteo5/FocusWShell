using System.Windows;
using System.Windows.Threading;
using FocusWShell.Win32;
using Forms = System.Windows.Forms;

namespace FocusWShell.App;

/// <summary>
/// Arranque del gestor: instancia única, recuperación del diario, icono de bandeja y atajo de emergencia.
/// Al salir (o ante una excepción no controlada) restaura todas las ventanas (CA6).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Application no es IDisposable; se liberan en OnExit.")]
public partial class App : Application
{
    private readonly ShellController _controller = new();
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private EmergencyHotkey? _hotkey;
    private MainWindow? _main;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\FocusWShell", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("FocusWShell ya se está ejecutando. Búscalo en el área de notificación.", "FocusWShell");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, _) => TryRestoreAll();

        OfferRecovery();

        _main = new MainWindow(_controller);
        _tray = new TrayIcon(ShowMain, RestoreFromTray, ExitAndRestore);
        _hotkey = EmergencyHotkey.Start(OnEmergencyHotkey);
        if (!_hotkey.IsRegistered)
        {
            _tray.Notify("Atajo de emergencia no disponible",
                $"Otra aplicación ya usa {EmergencyHotkey.Description}. Usa \"Restaurar todas\" en el menú de la bandeja.",
                Forms.ToolTipIcon.Warning);
        }

        ShowMain();
    }

    /// <summary>Arranque tras un cierre no limpio (ADR 0005): por defecto, restaurar.</summary>
    private void OfferRecovery()
    {
        int pending = _controller.PendingCount;
        if (pending == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"FocusWShell no se cerró bien la última vez y {pending} ventana(s) siguen fuera de su sitio original.\n\n" +
            "¿Restaurarlas ahora?\n\n(Si eliges No, se quedan donde están y podrás restaurarlas más tarde.)",
            "FocusWShell — recuperación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes)
        {
            var report = _controller.RestoreAll();
            if (!report.IsComplete)
            {
                MessageBox.Show(global::FocusWShell.App.MainWindow.Describe(report), "FocusWShell — recuperación");
            }
        }
    }

    private void ShowMain() => _main?.ShowAndActivate();

    private void RestoreFromTray()
    {
        var report = _controller.RestoreAll();
        _tray?.Notify("Restaurar todas", report.Entries.Count == 0 ? "No había nada que restaurar." : report.Summary);
        _main?.Refresh();
    }

    /// <summary>Llega en el hilo del atajo: restaura sin pasar por la UI y luego avisa.</summary>
    private void OnEmergencyHotkey()
    {
        var report = _controller.RestoreAll();
        Dispatcher.BeginInvoke(() =>
        {
            _tray?.Notify("Emergencia: ventanas restauradas", report.Entries.Count == 0 ? "No había nada que restaurar." : report.Summary);
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
            }
        });
    }

    private void ExitAndRestore()
    {
        IsExiting = true;
        TryRestoreAll();
        Shutdown();
    }

    private void TryRestoreAll()
    {
        try
        {
            _controller.RestoreAll();
        }
#pragma warning disable CA1031 // Último recurso: nunca debe impedir que el proceso termine.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryRestoreAll();
        MessageBox.Show(
            $"Error inesperado. Las ventanas se han devuelto a su sitio.\n\n{e.Exception.Message}",
            "FocusWShell", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }
}
