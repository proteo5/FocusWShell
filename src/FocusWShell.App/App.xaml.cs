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
    // El idioma, antes de crear cualquier texto (también los del controlador, que es un campo).
    static App() => Localization.Apply(AppSettings.Load().Language);

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

    /// <summary>Otra instancia pide mostrar la ventana principal (al volver a abrir FocusWShell).</summary>
    private const string ShowEventName = @"Local\FocusWShell.Mostrar";
    private EventWaitHandle? _showRequested;
    private RegisteredWaitHandle? _showWait;

    /// <summary>Dibuja cada sección de la ventana principal a un PNG, fuera de pantalla.</summary>
    private void RenderMainPreview(string folder, string? theme)
    {
        System.IO.Directory.CreateDirectory(folder);
        _controller.LoadPanels();
        _controller.LoadCatalog();
        _settings = AppSettings.Load();
        HotkeyMap = Hotkeys.Resolve(_settings.Hotkeys);
        var window = new MainWindow(_controller, _settings) { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        if (theme is "dark" or "light")
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme == "dark" ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        }

        for (int i = 0; i < window.Nav.Items.Count; i++)
        {
            window.Nav.SelectedIndex = i;
            window.UpdateLayout();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var tag = ((System.Windows.Controls.ListBoxItem)window.Nav.Items[i]).Tag;
            var root = (System.Windows.FrameworkElement)window.Content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            // Mica no se dibuja fuera de pantalla: se pone el fondo del tema detrás.
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var background = TryFindResource("ApplicationBackgroundBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
                dc.DrawRectangle(background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }

            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"main-{tag}.png"));
            encoder.Save(file);
        }
    }
    private RegisteredWaitHandle? _exitWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "FocusWShell.exe --deck-preview <carpeta>": PNGs del deck con datos de ejemplo, sin tocar nada más.
        int preview = Array.IndexOf(e.Args, "--deck-preview");
        if (preview >= 0)
        {
            DeckPreview.Run(
                preview + 1 < e.Args.Length ? e.Args[preview + 1] : System.IO.Path.GetTempPath(),
                preview + 2 < e.Args.Length ? e.Args[preview + 2] : null);
            Shutdown();
            return;
        }

        // "FocusWShell.exe --main-preview <carpeta> [es|en]": PNGs de cada sección de la ventana principal, con
        // los paneles y ventanas de verdad pero sin mover nada (para revisar el diseño).
        int mainPreview = Array.IndexOf(e.Args, "--main-preview");
        if (mainPreview >= 0)
        {
            if (mainPreview + 2 < e.Args.Length)
            {
                Localization.Apply(e.Args[mainPreview + 2]);
            }

            RenderMainPreview(
                mainPreview + 1 < e.Args.Length ? e.Args[mainPreview + 1] : System.IO.Path.GetTempPath(),
                mainPreview + 3 < e.Args.Length ? e.Args[mainPreview + 3] : null);
            Shutdown();
            return;
        }

        // "FocusWShell.exe --salir": pide a la instancia abierta que salga devolviendo las ventanas (lo usa
        // build\instalar.ps1 antes de reemplazar la copia instalada).
        bool exitRequest = e.Args.Any(a => a is "--salir" or "--exit");

        // "--reinicio": la abrió la instancia anterior al reiniciarse; espera a que termine de cerrarse.
        if (e.Args.Contains("--reinicio"))
        {
            var until = DateTime.Now.AddSeconds(20);
            while (Mutex.TryOpenExisting(@"Local\FocusWShell", out var previous) && DateTime.Now < until)
            {
                previous.Dispose();
                Thread.Sleep(300);
            }
        }

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
            // Ya hay uno abierto: se le pide que muestre su ventana (al frente) en lugar de un aviso.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
            {
                using (showEvent)
                {
                    showEvent.Set();
                }
            }
            else
            {
                MessageBox.Show(Strings.App_AlreadyRunning, "FocusWShell");
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitRequested, (_, _) => Dispatcher.BeginInvoke(ExitAndRestore), null, Timeout.Infinite, executeOnlyOnce: true);
        _showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showRequested, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, executeOnlyOnce: false);

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
        _controller.ForegroundChanged += hwnd => Dispatcher.BeginInvoke(() =>
        {
            _lists?.OnForeground(hwnd);
            _deck?.UpdateForeground(hwnd); // el indicador de «al frente», sin esperar al siguiente repaso
        });
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
            _tray?.Notify(Strings.Tray_Scene, message);
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
                        _tray?.Notify(resumed ? Strings.Tray_AfterSleep : Strings.Tray_Monitors, string.Join(" ", events));
                    }

                    if (_main?.IsVisible == true)
                    {
                        _main.Refresh();
                        if (events.Count > 0 || resumed)
                        {
                            var lines = resumed ? events.Prepend(Strings.Status_Resumed) : events;
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
        // La última sesión: cada ventana a su panel, como estaba. Las apps que se abran después (al iniciar
        // Windows suelen tardar) se adoptan durante unos minutos.
        Task.Run(() =>
        {
            var restored = _controller.RestoreSession();
            _controller.ShowLists();
            Dispatcher.BeginInvoke(() =>
            {
                if (restored is not null)
                {
                    RefreshAfter(restored);

                    // Restaurar la sesión activa la app del foco; acabas de abrir FocusWShell: su ventana, delante.
                    if (_main?.IsVisible == true)
                    {
                        ShowMain();
                    }
                }

                _sessionReady = true;
                StartLateAdoption();
            });
        });
        _controller.LayoutChanged += () => Dispatcher.BeginInvoke(ScheduleSessionSave);

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
            ["al-foco"] = () => OnCommandHotkey(Core.Strings.Action_ToFocus, _controller.PromoteForeground),
            ["anterior"] = () => OnCommandHotkey(Core.Strings.Action_Previous, () => _controller.Execute(new SwapWithPrevious())),
            ["foco-al-panel"] = () => OnCommandHotkey(Core.Strings.Action_FocusToPanel, _controller.ReturnCenter),
            ["pantalla-completa"] = () => OnCommandHotkey(Core.Strings.Action_Fullscreen, () => _controller.ToggleFullscreen()),
            ["vista"] = () => OnCommandHotkey(Core.Strings.Action_View, () =>
                _controller.Execute(new SetView(MainArrangement.Next(_controller.View, ShellController.UltrawideNow())))),
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
            warnings.Add(Core.Text.F(Strings.Hotkeys_InUse, string.Join(", ", _hotkeys.Failed.Select(f => $"{f.Description} ({f.Name})"))));
        }

        if (announce && warnings.Count > 0)
        {
            _tray?.Notify(Strings.Tray_Hotkeys, string.Join(" ", warnings), Forms.ToolTipIcon.Warning);
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
                var result = await Task.Run(() =>
                {
                    var r = _controller.Execute(commands);

                    // Elegir en el deck qué va al foco es para trabajar en ello: la app queda en primer
                    // plano y el cursor, encima, aunque el toque viniera de la tablet.
                    if (commands.Any(c => c is Promote or SwapWithPrevious or EnterFullscreen) && _controller.FocusedWindow() is { } focused)
                    {
                        WindowMover.Activate(focused);
                        WindowMover.PointAt(focused);
                    }

                    return r;
                });
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
            ToggleKeepAwake: ToggleKeepAwake,
            KeepAwake: () => _keepAwake.IsActive,
            FreeAll: RestoreFromTray,
            CanRearrange: () => _controller.CanRearrange,
            RearrangeAll: () => Task.Run(() =>
            {
                var result = _controller.RearrangeAll();
                Dispatcher.BeginInvoke(() =>
                {
                    _main?.Refresh();
                    PresentDeck();
                });
                return result;
            }),
            Power: PowerAction,
            Hide: () => _deck?.Hide(),
            Dock: dock => DockDeck(dock),
            CurrentDock: () => _controller.DeckDock,
            CloseApp: async handle =>
            {
                // Como su X: la app decide (y pregunta si tiene algo sin guardar). Primero se trae al frente, para
                // que esa pregunta, si la hay, se vea. Al cerrarse, el observador lo ve y el deck se actualiza.
                await Task.Run(() =>
                {
                    WindowMover.Activate(handle);
                    WindowMover.Close(handle);
                });
                await Task.Delay(1500);
                PresentDeck();
            },
            BringToFront: handle => Task.Run(() =>
            {
                // Al frente, con el cursor encima, sin gestionarla.
                WindowMover.Activate(handle);
                WindowMover.PointAt(handle);
            }),
            MouseToFocus: () => Task.Run(() =>
            {
                // Al centro de la app del foco (donde se va a trabajar) o, si no hay, al del principal: tras
                // usar el deck con el dedo, el cursor se queda en la tablet.
                if (_controller.FocusedWindow() is { } focus && WindowMover.PointAt(focus))
                {
                    return;
                }

                var area = ShellController.TargetDisplay().WorkArea;
                WindowMover.MoveCursorTo(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
            }),
            SetPinned: (appKey, pinned) =>
            {
                _controller.SetAppPinned(appKey, pinned);
                PresentDeck();
            },
            ReadPages: () => Task.Run(_controller.BrowserPages),
            SelectPage: (handle, tab) => Task.Run(() => ShellController.SelectPage(handle, tab)),
            ClosePage: (handle, tab) => Task.Run(() => _controller.ClosePage(handle, tab))));

        PresentDeck();
        StartDeckPolling();
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

    private DispatcherTimer? _deckPoll;
    private string? _deckSignature;

    /// <summary>
    /// Mientras el deck está a la vista, cada 3 s mira si se abrió o cerró alguna app y, sólo entonces, lo
    /// redibuja. Sin esto, una app recién abierta no salía entre las libres hasta otro cambio de layout.
    /// </summary>
    private void StartDeckPolling()
    {
        _deckPoll ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _deckPoll.Tick -= PollDeck;
        _deckPoll.Tick += PollDeck;
        _deckPoll.Start();
    }

    private void PollDeck(object? sender, EventArgs e)
    {
        if (_deck?.IsVisible != true || IsExiting)
        {
            return;
        }

        Task.Run(() =>
        {
            _controller.Refresh();
            var state = _controller.DeckState();
            var signature = string.Join("|", state.Panels.SelectMany(p => p.Apps).Concat(state.Free)
                .Select(a => $"{a.AppKey}:{a.Handle}:{a.State}:{a.PanelId}")) + $"|{state.Focus?.Handle}|{state.Foreground}";
            Dispatcher.BeginInvoke(() =>
            {
                if (signature != _deckSignature)
                {
                    _deckSignature = signature;
                    _deck?.Update(state);
                }
            });
        });
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
            Core.Text.F(Strings.Recovery_Ask, pending),
            Strings.Recovery_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes)
        {
            var report = _controller.RestoreAllWindows();
            if (!report.IsComplete)
            {
                MessageBox.Show(global::FocusWShell.App.MainWindow.Describe(report), Strings.Recovery_Title);
            }
        }
    }

    private void ShowMain()
    {
        if (_main is null)
        {
            return;
        }

        _main.ShowAndActivate();

        // WPF la centra en el monitor del cursor. Si usas el deck en la tablet, el cursor está ahí y la
        // ventana quedaba detrás del deck (que va siempre encima). Si cae donde está el deck, va al centro
        // del monitor principal.
        // Vale también al arrancar, cuando el deck fijado aún no se ve: se mira el monitor guardado.
        var handle = new System.Windows.Interop.WindowInteropHelper(_main).Handle;
        var rect = WindowMover.Observe(handle).Visible;
        var deckMonitor = _settings.DeckDisplay is { } key
            ? ShellController.AuxiliaryDisplays().FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))?.Bounds
            : null;
        var deckArea = deckMonitor ?? (_deck?.IsVisible == true ? DeckPlace() : (Core.Geometry.PixelRect?)null);
        if (deckArea is { } area0 && !rect.IsEmpty && area0.Contains(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2)))
        {
            var area = ShellController.TargetDisplay().WorkArea;
            int width = Math.Min(rect.Width, area.Width);
            int height = Math.Min(rect.Height, area.Height);
            OwnWindows.Place(handle, Core.Geometry.PixelRect.FromSize(
                area.Left + ((area.Width - width) / 2), area.Top + ((area.Height - height) / 2), width, height));
            WindowMover.Activate(handle);
        }
    }

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
            _tray?.Notify(Core.Text.F(Strings.Tray_SceneName, result.Name), result.Missing.Count == 0 && result.Notes.Count == 0
                ? Strings.Tray_Applied
                : string.Join(" ", result.Summary.Split(Environment.NewLine).Skip(1)));
            if (_main?.IsVisible == true)
            {
                _main.Refresh();
                _main.SetStatus(result.Summary + Environment.NewLine + global::FocusWShell.App.MainWindow.Describe(result.Result));
            }
        });
    }

    private void ToggleKeepAwake() => SetKeepAwake(!_keepAwake.IsActive, announce: true);

    /// <summary>Para la ventana principal (General): si la pantalla se mantiene encendida, y cambiarlo.</summary>
    internal bool KeepAwakeActive => _keepAwake.IsActive;

    internal void SetKeepAwakeFromMain(bool active) => SetKeepAwake(active, announce: false);

    /// <summary>Para la ventana principal (General): si esta copia inicia con Windows, y cambiarlo.</summary>
    internal static bool StartsWithWindows => StartWithWindows.IsEnabledFor(ExePath);

    internal void SetStartWithWindows(bool enabled)
    {
        if (enabled != StartsWithWindows)
        {
            ToggleStartup();
        }
    }

    /// <summary>Mantener la pantalla encendida (ADR 0010): aplica, recuerda y lo refleja en la bandeja.</summary>
    private void SetKeepAwake(bool active, bool announce)
    {
        if (!_keepAwake.Set(active))
        {
            _tray?.Notify(Strings.Tray_KeepAwake, Strings.Tray_KeepAwakeFailed, Forms.ToolTipIcon.Warning);
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
                active ? Strings.Tray_AwakeOn : Strings.Tray_AwakeOff,
                active
                    ? Strings.Tray_AwakeOnText
                    : Strings.Tray_AwakeOffText);
        }
    }

    private void RestoreFromTray()
    {
        var report = _controller.FreeAllWindows();
        _tray?.Notify(Strings.Tray_RestoreAll, report.Entries.Count == 0 ? Strings.Tray_NothingToRestore : report.Summary);
        _main?.Refresh();
    }

    /// <summary>
    /// Energía desde el deck. Ninguna libera las ventanas: antes de reiniciar, apagar o cerrar sesión se
    /// guarda la sesión, y al volver FocusWShell las pone otra vez en sus paneles.
    /// </summary>
    private void PowerAction(DeckPower action)
    {
        if (action is DeckPower.Restart or DeckPower.ShutDown or DeckPower.SignOut && _sessionReady)
        {
            _sessionSave?.Stop();
            _controller.SaveSession();
        }

        _deck?.Hide();
        switch (action)
        {
            case DeckPower.Lock:
                Power.Lock();
                break;
            case DeckPower.Sleep:
                Power.Sleep();
                break;
            case DeckPower.Restart:
                Power.Restart();
                break;
            case DeckPower.ShutDown:
                Power.ShutDown();
                break;
            case DeckPower.SignOut:
                Power.SignOut();
                break;
        }
    }

    /// <summary>Llega en el hilo del atajo: restaura sin pasar por la UI y luego avisa.</summary>
    private void OnEmergencyHotkey()
    {
        var report = _controller.FreeAllWindows();
        Dispatcher.BeginInvoke(() =>
        {
            _tray?.Notify(Strings.Tray_Emergency, report.Entries.Count == 0 ? Strings.Tray_NothingToRestore : report.Summary);
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
        _tray?.Notify(Strings.Tray_StartWithWindows, enable ? Core.Text.F(Strings.Tray_StartOn, ExePath) : Strings.Tray_StartOff);
    }

    /// <summary>La sesión se guarda sólo después de restaurarla al arrancar (si no, se guardaría vacía).</summary>
    private bool _sessionReady;
    private DispatcherTimer? _sessionSave;

    /// <summary>Guarda la sesión unos segundos después del último cambio.</summary>
    private void ScheduleSessionSave()
    {
        if (!_sessionReady || IsExiting)
        {
            return;
        }

        _sessionSave ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _sessionSave.Stop();
        _sessionSave.Tick -= SaveSessionTick;
        _sessionSave.Tick += SaveSessionTick;
        _sessionSave.Start();
    }

    private void SaveSessionTick(object? sender, EventArgs e)
    {
        _sessionSave?.Stop();
        Task.Run(_controller.SaveSession);
    }

    /// <summary>Cada 10 s durante 3 min, adopta las ventanas de la última sesión que aparezcan.</summary>
    private void StartLateAdoption()
    {
        var until = DateTime.Now.AddMinutes(3);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            if (DateTime.Now > until || IsExiting)
            {
                timer.Stop();
                return;
            }

            Task.Run(() =>
            {
                if (_controller.AdoptLateSessionWindows() is { } result)
                {
                    Dispatcher.BeginInvoke(() => RefreshAfter(result));
                }
            });
        };
        timer.Start();
    }

    /// <summary>Reinicia FocusWShell (p. ej. para cambiar de idioma): sale como «Salir» y abre otra instancia.</summary>
    public void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExePath, "--reinicio") { UseShellExecute = false });
        ExitAndRestore();
    }

    private void ExitAndRestore()
    {
        // Antes de devolver las ventanas a su sitio: así, al volver a abrir FocusWShell, vuelven a sus paneles.
        if (_sessionReady)
        {
            _sessionSave?.Stop();
            _controller.SaveSession();
        }

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
            Core.Text.F(Strings.App_UnexpectedError, e.Exception.Message),
            "FocusWShell", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitWait?.Unregister(null);
        _exitRequested?.Dispose();
        _showWait?.Unregister(null);
        _showRequested?.Dispose();
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
