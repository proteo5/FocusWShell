using System.Windows;
using System.Windows.Threading;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Input;
using FocusWShell.Core.Layout;
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
    private ListManager? _lists;
    private DisplayChangeListener? _displays;
    private readonly DisplayKeepAwake _keepAwake = new();
    private AppSettings _settings = new();

    public bool IsExiting { get; private set; }

    private const string ExitEventName = @"Local\FocusWShell.Salir";
    private EventWaitHandle? _exitRequested;
    private RegisteredWaitHandle? _exitWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "FocusWShell.exe --salir": pide a la instancia abierta que salga devolviendo las ventanas (lo usa
        // build\instalar.ps1 antes de reemplazar la copia instalada).
        bool exitRequest = e.Args.Any(a => a is "--salir" or "--exit");
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\FocusWShell", out bool createdNew);
        if (exitRequest)
        {
            if (!createdNew && EventWaitHandle.TryOpenExisting(ExitEventName, out var exitEvent))
            {
                using (exitEvent)
                {
                    exitEvent.Set();
                }
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        if (!createdNew)
        {
            MessageBox.Show("FocusWShell ya se está ejecutando. Búscalo en el área de notificación.", "FocusWShell");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitRequested, (_, _) => Dispatcher.BeginInvoke(ExitAndRestore), null, Timeout.Infinite, executeOnlyOnce: true);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogError(args.ExceptionObject as Exception);
            TryRestoreAll();
        };

        OfferRecovery();

        _controller.LoadPanels();
        _controller.LoadCatalog();
        _settings = AppSettings.Load();
        _main = new MainWindow(_controller, _settings);

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
        // Listas tipo Explorador (ADR 0015): también cambian con el catálogo (apps abiertas o cerradas).
        _lists = new ListManager(
            _controller,
            result => Dispatcher.BeginInvoke(() =>
            {
                if (_main?.IsVisible == true)
                {
                    _main.Refresh();
                    _main.SetStatus(global::FocusWShell.App.MainWindow.Describe(result));
                }
            }),
            message => Dispatcher.BeginInvoke(() => _main?.SetStatus(message)));
        _controller.LayoutChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _strips?.Refresh();
            _lists?.Refresh();
            RefreshDeck();
        });
        _controller.CatalogChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _lists?.Refresh();
            RefreshDeck();
        });
        _controller.ForegroundChanged += hwnd => Dispatcher.BeginInvoke(() => _lists?.OnForeground(hwnd));
        _controller.PauseChanged += _ => Dispatcher.BeginInvoke(FollowPause);
        _controller.FocusGestureEnabled = g => g switch
        {
            SwitchGesture.AltTab => _settings.FocusOnAltTab,
            SwitchGesture.TaskView => _settings.FocusOnTaskView,
            SwitchGesture.Taskbar => _settings.FocusOnTaskbar,
            SwitchGesture.Click => _settings.FocusOnClick,
            _ => false,
        };
        _controller.FocusGestureHandled += (message, result) => Dispatcher.BeginInvoke(() =>
        {
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(message + Environment.NewLine + global::FocusWShell.App.MainWindow.Describe(result));
            }
        });
        _controller.OpenMissingApps = () => _settings.OpenMissingApps;
        _controller.SetExclusions(_settings.Exclusions);
        _controller.SceneAppsOpened += (message, result) => Dispatcher.BeginInvoke(() =>
        {
            _tray?.Notify("Escena", message);
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(message + Environment.NewLine + global::FocusWShell.App.MainWindow.Describe(result));
            }
        });
        _controller.StartWatching();

        // S6: monitores que se conectan o desconectan (también tablets por wifi). M2: y la suspensión.
        _displays = new DisplayChangeListener(
            _controller.BeginDisplayChange,
            resumed => Task.Run(() =>
            {
                var (events, result) = _controller.EndDisplayChange();
                Dispatcher.BeginInvoke(() =>
                {
                    FollowDeckMonitor();
                    if (events.Count > 0)
                    {
                        _tray?.Notify(resumed ? "Tras la suspensión" : "Monitores", string.Join(" ", events));
                    }

                    if (_main?.IsVisible == true)
                    {
                        _main.Refresh();
                        if (events.Count > 0 || resumed)
                        {
                            var lines = resumed ? events.Prepend("Volvió de la suspensión: se revisaron ventanas y monitores.") : events;
                            _main.SetStatus(string.Join(Environment.NewLine, lines) +
                                (result is null ? "" : Environment.NewLine + global::FocusWShell.App.MainWindow.Describe(result)));
                        }
                    }
                });
            }));
        _tray = new TrayIcon(ShowMain, ToggleKeepAwake, RestoreFromTray, ExitAndRestore, () => Dispatcher.BeginInvoke(ToggleDeck), ToggleStartup);
        ShowStartup();
        SetKeepAwake(_settings.KeepDisplayOn, announce: false);
        RefreshTrayScenes();
        _controller.ScenesChanged += () => Dispatcher.BeginInvoke(RefreshTrayScenes);
        StartHotkeys(announce: true);

        ShowMain();
        Task.Run(_controller.ShowLists);

        // El deck fijado en un monitor (ADR 0013, fase 4) vuelve a su sitio al arrancar, si el monitor está.
        if (_settings.DeckDisplay is { } deckDisplay && Enum.TryParse<DeckZone>(_settings.DeckZone, out var zone))
        {
            Task.Run(() =>
            {
                _controller.SetDeckDock(new DeckDock(deckDisplay, zone));
                Dispatcher.BeginInvoke(FollowDeckMonitor);
            });
        }
    }

    /// <summary>Atajos en uso: los de siempre con los cambios del usuario (M1, atajos configurables).</summary>
    public HotkeyMap HotkeyMap { get; private set; } = Hotkeys.Resolve(null);

    /// <summary>Texto del atajo de una acción para la UI, o "" si no tiene.</summary>
    public string ChordOf(string actionId) => HotkeyMap.Chords.TryGetValue(actionId, out var chord) ? chord.Display() : "";

    /// <summary>
    /// (Re)registra los atajos según la configuración. Avisa de los que no valen y de los que otra app
    /// ya usa. Devuelve los avisos.
    /// </summary>
    public IReadOnlyList<string> StartHotkeys(bool announce)
    {
        _hotkeys?.Dispose();
        HotkeyMap = Hotkeys.Resolve(_settings.Hotkeys);
        var actions = new Dictionary<string, Action>
        {
            [Hotkeys.Emergency] = OnEmergencyHotkey,
            ["al-foco"] = () => OnCommandHotkey("Al foco", _controller.PromoteForeground),
            ["anterior"] = () => OnCommandHotkey("Volver a la anterior", () => _controller.Execute(new SwapWithPrevious())),
            ["foco-al-panel"] = () => OnCommandHotkey("Foco al panel", _controller.ReturnCenter),
            ["pantalla-completa"] = () => OnCommandHotkey("Pantalla completa", () => _controller.ToggleFullscreen()),
            ["vista"] = () => OnCommandHotkey("Cambiar de vista", () =>
                _controller.Execute(new SetView(_controller.View switch
                {
                    FocusPosition.Center => FocusPosition.Left,
                    FocusPosition.Left => FocusPosition.Right,
                    _ => FocusPosition.Center,
                }))),
            ["pantalla-encendida"] = () => Dispatcher.BeginInvoke(ToggleKeepAwake),
            [Hotkeys.Deck] = () => Dispatcher.BeginInvoke(ToggleDeck),
        };
        for (int n = 1; n <= 9; n++)
        {
            int index = n - 1;
            actions[$"escena-{n}"] = () => ApplySceneByIndex(index);
        }

        var bindings = Hotkeys.Actions
            .Where(a => HotkeyMap.Chords.ContainsKey(a.Id) && actions.ContainsKey(a.Id))
            .Select(a => new HotkeyBinding(a.Name, (uint)HotkeyMap.Chords[a.Id].Modifiers, HotkeyMap.Chords[a.Id].VirtualKey, HotkeyMap.Chords[a.Id].Display(), actions[a.Id]))
            .ToList();
        _hotkeys = GlobalHotkeys.Start(bindings);

        var warnings = new List<string>(HotkeyMap.Problems);
        if (_hotkeys.Failed.Count > 0)
        {
            warnings.Add($"Otra aplicación ya usa: {string.Join(", ", _hotkeys.Failed.Select(f => $"{f.Description} ({f.Name})"))}. \"Restaurar todas\" sigue en el menú de la bandeja.");
        }

        if (announce && warnings.Count > 0)
        {
            _tray?.Notify("Atajos", string.Join(" ", warnings), Forms.ToolTipIcon.Warning);
        }

        _tray?.SetHotkeys(ChordOf("pantalla-encendida"), ChordOf(Hotkeys.Emergency), n => ChordOf($"escena-{n}"));
        RefreshTrayScenes();
        return warnings;
    }

    private DeckWindow? _deck;

    /// <summary>Muestra el deck (ADR 0013) en la esquina inferior derecha del principal, o lo oculta.</summary>
    public void ToggleDeck()
    {
        if (_deck?.IsVisible == true)
        {
            _deck.Hide();
            return;
        }

        _deck ??= new DeckWindow(new DeckActions(
            Run: async commands =>
            {
                var result = await Task.Run(() => _controller.Execute(commands));
                if (result.Rejections.Count > 0)
                {
                    _tray?.Notify("Deck", string.Join(" ", result.Rejections), Forms.ToolTipIcon.None);
                }

                RefreshAfter(result);
            },
            OpenApp: async (appKey, panelId) =>
            {
                var message = await Task.Run(() => _controller.OpenApp(appKey, panelId));
                _tray?.Notify("Deck", message);
                _main?.Refresh();
            },
            ApplyScene: name => Task.Run(() => ApplyScene(name)),
            ToggleKeepAwake: ToggleKeepAwake,
            KeepAwake: () => _keepAwake.IsActive,
            RestoreAll: RestoreFromTray,
            Hide: () => _deck?.Hide(),
            Dock: dock => DockDeck(dock),
            CurrentDock: () => _controller.DeckDock));

        PresentDeck();
    }

    /// <summary>Lo muestra donde toca: fijado en su monitor si está conectado; si no, en la esquina del principal.</summary>
    private void PresentDeck()
    {
        Task.Run(() =>
        {
            _controller.Refresh();
            var state = _controller.DeckState();
            var area = DeckPlace();
            Dispatcher.BeginInvoke(() => _deck?.Present(state, area));
        });
    }

    /// <summary>Dónde va el deck ahora: su zona en el monitor donde está fijado, o la esquina del principal.</summary>
    private Core.Geometry.PixelRect DeckPlace()
    {
        if (_controller.DeckDock is { } dock
            && ShellController.AuxiliaryDisplays().FirstOrDefault(d => string.Equals(d.Key, dock.Display, StringComparison.OrdinalIgnoreCase)) is { } display)
        {
            return DeckDock.Split(display.WorkArea, dock.Zone).Deck;
        }

        return DeckArea(ShellController.TargetDisplay());
    }

    /// <summary>"Mandar el deck a…": se guarda, los paneles de ese monitor dejan su sitio y el deck se mueve.</summary>
    private void DockDeck(DeckDock? dock)
    {
        _settings.DeckDisplay = dock?.Display;
        _settings.DeckZone = dock?.Zone.ToString();
        _settings.Save();
        Task.Run(() =>
        {
            var result = _controller.SetDeckDock(dock);
            Dispatcher.BeginInvoke(() =>
            {
                RefreshAfter(result);
                if (dock is null)
                {
                    _deck?.Hide();
                }
                else
                {
                    PresentDeck();
                }
            });
        });
    }

    /// <summary>
    /// Tras un cambio de monitores: si el deck está fijado y su monitor se fue, se oculta; si volvió (o
    /// aparece al arrancar), se muestra allí.
    /// </summary>
    private void FollowDeckMonitor()
    {
        if (_controller.DeckDock is not { } dock)
        {
            return;
        }

        bool present = ShellController.AuxiliaryDisplays().Any(d => string.Equals(d.Key, dock.Display, StringComparison.OrdinalIgnoreCase));
        if (!present)
        {
            _deck?.Hide();
        }
        else if (_deck?.IsVisible != true)
        {
            ToggleDeck();
        }
        else
        {
            PresentDeck();
        }
    }

    /// <summary>El deck se ocultó porque una app ocupa su monitor en pantalla completa.</summary>
    private bool _deckPaused;

    /// <summary>
    /// Pantalla completa propia de una app (M2): el deck no debe quedar encima. Se oculta si está en ese
    /// monitor y vuelve cuando termina.
    /// </summary>
    private void FollowPause()
    {
        if (_deck is null)
        {
            return;
        }

        if (_deck.IsVisible && _controller.IsPaused(DeckPlace()))
        {
            _deck.Hide();
            _deckPaused = true;
        }
        else if (_deckPaused && !_controller.IsPaused(DeckPlace()))
        {
            _deckPaused = false;
            PresentDeck();
        }
    }

    /// <summary>Esquina inferior derecha del área de trabajo del principal, con margen.</summary>
    private static Core.Geometry.PixelRect DeckArea(Core.Displays.DisplayInfo display)
    {
        double scale = display.Dpi / 96.0;
        var area = display.WorkArea;
        int margin = (int)(16 * scale);
        int width = Math.Min((int)(640 * scale), area.Width - (2 * margin));
        int height = Math.Min((int)(980 * scale), area.Height - (2 * margin));
        return Core.Geometry.PixelRect.FromSize(area.Right - width - margin, area.Bottom - height - margin, width, height);
    }

    /// <summary>Si el deck está a la vista, lo pone al día (otro panel, otra app abierta…).</summary>
    private void RefreshDeck()
    {
        if (_deck?.IsVisible != true)
        {
            return;
        }

        Task.Run(() =>
        {
            var state = _controller.DeckState();
            Dispatcher.BeginInvoke(() => _deck?.Update(state));
        });
    }

    private void RefreshAfter(ControllerResult result)
    {
        if (_main?.IsVisible == true)
        {
            _main.Refresh();
            _main.SetStatus(global::FocusWShell.App.MainWindow.Describe(result));
        }

        RefreshDeck();
    }

    /// <summary>Deja de escuchar los atajos (mientras se eligen otros: si no, Windows se quedaría las teclas).</summary>
    public void PauseHotkeys()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
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

    /// <summary>Atajo de la escena N: llega en el hilo de atajos.</summary>
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

    private static string ExePath => Environment.ProcessPath ?? "";

    private void ShowStartup()
    {
        var registered = StartWithWindows.RegisteredPath();
        bool mine = StartWithWindows.IsEnabledFor(ExePath);
        _tray?.ShowStartup(mine, mine ? null : registered);
    }

    /// <summary>
    /// Iniciar con Windows este ejecutable, o dejar de hacerlo. Si estaba registrada otra copia (p. ej. la
    /// de desarrollo), marcarlo la cambia por ésta.
    /// </summary>
    private void ToggleStartup()
    {
        bool enable = !StartWithWindows.IsEnabledFor(ExePath);
        if (enable)
        {
            StartWithWindows.Enable(ExePath);
        }
        else
        {
            StartWithWindows.Disable();
        }

        ShowStartup();
        _tray?.Notify("Iniciar con Windows", enable ? $"FocusWShell se abrirá al iniciar sesión ({ExePath})." : "FocusWShell ya no se abrirá al iniciar sesión.");
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

    /// <summary>Deja constancia del error en <c>%LOCALAPPDATA%\FocusWShell\errors.log</c> (nunca falla).</summary>
    private static void LogError(Exception? exception)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusWShell", "errors.log");
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
#pragma warning disable CA1031 // Registrar un error nunca debe provocar otro.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        TryRestoreAll();
        MessageBox.Show(
            $"Error inesperado. Las ventanas se han devuelto a su sitio.\n\n{e.Exception.Message}",
            "FocusWShell", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitWait?.Unregister(null);
        _exitRequested?.Dispose();
        _displays?.Dispose();
        _strips?.CloseAll();
        _lists?.CloseAll();
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
