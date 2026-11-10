using System.Windows;
using System.Windows.Threading;
using FocusWShell.Core.Domain;
using FocusWShell.Win32;
using Forms = System.Windows.Forms;

namespace FocusWShell.App;

/// <summary>
/// Arranque del gestor: instancia única, recuperación del diario, icono de bandeja y atajos globales.
/// Al salir (o ante una excepción no controlada) restaura todas las ventanas (CA6).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Application no es IDisposable; se liberan en OnExit.")]
public partial class App : Application
{
    private readonly ShellController _controller = new();
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private GlobalHotkeys? _hotkeys;
    private MainWindow? _main;
    private StripManager? _strips;
    private readonly DisplayKeepAwake _keepAwake = new();
    private AppSettings _settings = new();

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

        // S4: lo que haga el usuario con las ventanas gestionadas se traduce en comandos.
        _controller.ExternalChangeHandled += handled => Dispatcher.BeginInvoke(() =>
        {
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(handled.Describe() + Environment.NewLine + global::FocusWShell.App.MainWindow.Describe(handled.Result));
            }
        });
        // Tiras de pestañas: se rehacen cada vez que cambia el layout.
        _strips = new StripManager(_controller, result => Dispatcher.BeginInvoke(() =>
        {
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(global::FocusWShell.App.MainWindow.Describe(result));
            }
        }));
        _controller.LayoutChanged += () => Dispatcher.BeginInvoke(() => _strips?.Refresh());
        _controller.StartWatching();
        _settings = AppSettings.Load();
        _tray = new TrayIcon(ShowMain, ToggleKeepAwake, RestoreFromTray, ExitAndRestore);
        SetKeepAwake(_settings.KeepDisplayOn, announce: false);
        RefreshTrayScenes();
        _controller.ScenesChanged += () => Dispatcher.BeginInvoke(RefreshTrayScenes);
        _hotkeys = GlobalHotkeys.Start(
        [
            new("Restaurar todas", GlobalHotkeys.VkBack, "Ctrl+Alt+Mayús+Retroceso", OnEmergencyHotkey),
            new("Al Centro", GlobalHotkeys.VkUp, "Ctrl+Alt+Mayús+↑", () => OnCommandHotkey("Al Centro", _controller.PromoteForeground)),
            new("Volver a la anterior", GlobalHotkeys.VkLeft, "Ctrl+Alt+Mayús+←",
                () => OnCommandHotkey("Volver a la anterior", () => _controller.Execute(new SwapWithPrevious()))),
            new("Centro al panel", GlobalHotkeys.VkDown, "Ctrl+Alt+Mayús+↓", () => OnCommandHotkey("Centro al panel", _controller.ReturnCenter)),
            new("Pantalla encendida", GlobalHotkeys.VkP, "Ctrl+Alt+Mayús+P", () => Dispatcher.BeginInvoke(ToggleKeepAwake)),
            .. Enumerable.Range(1, 9).Select(n => new HotkeyBinding(
                $"Escena {n}", (uint)(0x30 + n), $"Ctrl+Alt+Mayús+{n}", () => ApplySceneByIndex(n - 1))),
        ]);
        if (_hotkeys.Failed.Count > 0)
        {
            _tray.Notify("Atajos no disponibles",
                $"Otra aplicación ya usa: {string.Join(", ", _hotkeys.Failed.Select(f => f.Description))}. " +
                "\"Restaurar todas\" sigue en el menú de la bandeja.",
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
            var report = _controller.RestoreAllWindows();
            if (!report.IsComplete)
            {
                MessageBox.Show(global::FocusWShell.App.MainWindow.Describe(report), "FocusWShell — recuperación");
            }
        }
    }

    private void ShowMain() => _main?.ShowAndActivate();

    private void RefreshTrayScenes() => _tray?.ShowScenes(_controller.SceneNames(), name => Task.Run(() => ApplyScene(name)));

    /// <summary>Atajo Ctrl+Alt+Mayús+N: llega en el hilo de atajos.</summary>
    private void ApplySceneByIndex(int index)
    {
        var names = _controller.SceneNames();
        if (index < names.Count)
        {
            ApplyScene(names[index]);
        }
    }

    private void ApplyScene(string name)
    {
        var result = _controller.ApplySavedScene(name);
        Dispatcher.BeginInvoke(() =>
        {
            _tray?.Notify($"Escena «{result.Name}»", result.Missing.Count == 0 && result.Notes.Count == 0
                ? "Aplicada."
                : string.Join(" ", result.Summary.Split(Environment.NewLine).Skip(1)));
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(result.Summary + Environment.NewLine + global::FocusWShell.App.MainWindow.Describe(result.Result));
            }
        });
    }

    private void ToggleKeepAwake() => SetKeepAwake(!_keepAwake.IsActive, announce: true);

    /// <summary>Mantener la pantalla encendida (ADR 0010): aplica, recuerda y lo refleja en la bandeja.</summary>
    private void SetKeepAwake(bool active, bool announce)
    {
        if (!_keepAwake.Set(active))
        {
            _tray?.Notify("Pantalla encendida", "Windows no aceptó la petición de energía.", Forms.ToolTipIcon.Warning);
            return;
        }

        _tray?.ShowKeepAwake(active);
        if (_settings.KeepDisplayOn != active)
        {
            _settings.KeepDisplayOn = active;
            _settings.Save();
        }

        if (announce)
        {
            _tray?.Notify(
                active ? "Pantalla siempre encendida" : "Pantalla con ahorro normal",
                active
                    ? "Ni la pantalla se apagará ni saltará el protector de pantalla mientras esté activo."
                    : "Windows vuelve a apagar la pantalla por inactividad como siempre.");
        }
    }

    private void RestoreFromTray()
    {
        var report = _controller.RestoreAllWindows();
        _tray?.Notify("Restaurar todas", report.Entries.Count == 0 ? "No había nada que restaurar." : report.Summary);
        _main?.Refresh();
    }

    /// <summary>Llega en el hilo del atajo: restaura sin pasar por la UI y luego avisa.</summary>
    private void OnEmergencyHotkey()
    {
        var report = _controller.RestoreAllWindows();
        Dispatcher.BeginInvoke(() =>
        {
            _tray?.Notify("Emergencia: ventanas restauradas", report.Entries.Count == 0 ? "No había nada que restaurar." : report.Summary);
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
            }
        });
    }

    /// <summary>Atajos de comando: se ejecutan en el hilo de atajos (el proceso acaba de recibir input, así que puede dar el foco).</summary>
    private void OnCommandHotkey(string name, Func<ControllerResult> command)
    {
        var result = command();
        Dispatcher.BeginInvoke(() =>
        {
            if (result.Rejections.Count > 0)
            {
                _tray?.Notify(name, string.Join(" ", result.Rejections), Forms.ToolTipIcon.None);
            }

            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(global::FocusWShell.App.MainWindow.Describe(result));
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
            _controller.RestoreAllWindows();
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
        _strips?.CloseAll();
        _hotkeys?.Dispose();
        _controller.Dispose();
        _keepAwake.Dispose();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }
}
