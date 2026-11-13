using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;
using FocusWShell.Core.Windows;
using FocusWShell.Win32.Persistence;
using FocusWShell.Win32.Scenes;
using Wpf.Ui.Controls;
using WC = System.Windows.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace FocusWShell.App;

/// <summary>
/// La ventana principal: configurar y ver el estado (el día a día va en el deck). Navegación lateral como la
/// Configuración de Windows, con WPF-UI. El estado real vive en el modelo del controlador.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ShellController _controller;
    private readonly ObservableCollection<WindowRow> _rows = [];

    /// <summary>Destinos que el usuario acaba de cambiar y aún no se han aplicado, por handle.</summary>
    private readonly Dictionary<nint, string> _pending = [];

    private bool _buildingPanels;

    /// <summary>Opciones de la columna Panel: libre, al foco y cada panel.</summary>
    public ObservableCollection<AssignmentOption> AssignmentOptions { get; } = [];

    private readonly AppSettings _settings;
    private bool _loadingGestures;

    /// <summary>El azul del foco en el deck (ActiveBrush), para el mapa de monitores.</summary>
    private static readonly SolidColorBrush FocusBlue = new(Color.FromRgb(0x28, 0x78, 0xDC));

    /// <summary>Cierra sola la franja de avisos.</summary>
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public MainWindow(ShellController controller, AppSettings settings)
    {
        _controller = controller;
        _settings = settings;
        InitializeComponent();
        Loaded += (_, _) => ApplyTheme(_settings.Theme);
        Title = AppVersion.Title;
        TitleBar.Title = AppVersion.Title;
        VersionText.Text = AppVersion.Title;
        AboutVersion.Text = Core.Text.F(Strings.Gen_Version, AppVersion.Display) + (AppVersion.Commit.Length > 0 ? $" · {AppVersion.Commit}" : "");
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusBar.IsOpen = false;
        };

        _loadingGestures = true;
        GestureAltTab.IsChecked = settings.FocusOnAltTab;
        GestureTaskView.IsChecked = settings.FocusOnTaskView;
        GestureTaskbar.IsChecked = settings.FocusOnTaskbar;
        GestureClick.IsChecked = settings.FocusOnClick;
        OpenMissingBox.IsChecked = settings.OpenMissingApps;
        StartupSwitch.IsChecked = App.StartsWithWindows;
        AwakeSwitch.IsChecked = ((App)Application.Current).KeepAwakeActive;
        LanguageCombo.SelectedItem = LanguageCombo.Items.Cast<WC.ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == settings.Language) ?? LanguageCombo.Items[0];
        ThemeCombo.SelectedItem = ThemeCombo.Items.Cast<WC.ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == settings.Theme) ?? ThemeCombo.Items[0];
        _loadingGestures = false;

        DataContext = this;
        WindowList.ItemsSource = _rows;
        Nav.SelectedIndex = 0;
        LoadScenes();
        _controller.ScenesChanged += () => Dispatcher.BeginInvoke(LoadScenes);
        _controller.CatalogChanged += () => Dispatcher.BeginInvoke(() => BuildCatalog(_controller.Panels()));
        BuildExclusions();
        Refresh();
    }

    // --- Navegación ----------------------------------------------------------------------------------

    private void Nav_SelectionChanged(object sender, WC.SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not WC.ListBoxItem { Tag: string page })
        {
            return;
        }

        foreach (var (name, section) in new (string, FrameworkElement)[]
        {
            ("Home", HomeSection), ("Windows", WindowsSection), ("Panels", PanelsSection), ("Apps", AppsSection),
            ("Shortcuts", ShortcutsSection), ("Scenes", ScenesSection), ("Excluded", ExcludedSection), ("General", GeneralSection),
        })
        {
            section.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        }

        if (page == "Shortcuts")
        {
            BuildShortcuts();
        }
    }

    /// <summary>Una tarjeta (fondo y borde del tema) alrededor de una fila.</summary>
    private static WC.Border CardRow(UIElement content)
    {
        var card = new WC.Border
        {
            Child = content,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(WC.Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(WC.Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    /// <summary>Un texto con un color del tema.</summary>
    private static WC.TextBlock Text(string text, string brush = "TextFillColorPrimaryBrush", double size = 14)
    {
        var block = new WC.TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(WC.TextBlock.ForegroundProperty, brush);
        return block;
    }

    // --- Lista de exclusión (M2) -------------------------------------------------------------------

    /// <summary>Las reglas, cada una en su fila con su botón para quitarla.</summary>
    private void BuildExclusions()
    {
        ExclusionsList.Children.Clear();
        if (_settings.Exclusions.Count == 0)
        {
            ExclusionsList.Children.Add(Text(Strings.Excl_None, "TextFillColorSecondaryBrush"));
        }

        foreach (var rule in _settings.Exclusions)
        {
            var row = new WC.DockPanel();
            var remove = new Wpf.Ui.Controls.Button { Content = Strings.Excl_Remove, Icon = new SymbolIcon(SymbolRegular.Delete24) };
            System.Windows.Automation.AutomationProperties.SetName(remove, Core.Text.F(Strings.Excl_RemoveName, rule.Describe()));
            remove.Click += (_, _) => SetExclusions(_settings.Exclusions.Where(r => r != rule).ToList(), Core.Text.F(Strings.Excl_Removed, rule.Describe()));
            WC.DockPanel.SetDock(remove, WC.Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(Text(rule.Describe()));
            ExclusionsList.Children.Add(CardRow(row));
        }
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        var value = ExclusionValueBox.Text.Trim();
        if (value.Length == 0)
        {
            SetStatus(Strings.Excl_TypeWhat);
            return;
        }

        var kind = (ExclusionKind)Math.Max(0, ExclusionKindCombo.SelectedIndex);
        AddExclusion(new ExclusionRule(kind, value));
        ExclusionValueBox.Clear();
    }

    private void AddExclusion(ExclusionRule rule)
    {
        if (_settings.Exclusions.Any(r => r.SameAs(rule)))
        {
            SetStatus(Core.Text.F(Strings.Excl_Already, rule.Describe()));
            return;
        }

        SetExclusions([.. _settings.Exclusions, rule], Core.Text.F(Strings.Excl_Added, rule.Describe()));
    }

    private void SetExclusions(List<ExclusionRule> rules, string message)
    {
        _settings.Exclusions = rules;
        _settings.Save();
        var result = _controller.SetExclusions(rules);
        BuildExclusions();
        Refresh();
        SetStatus(message + (result.Applied is null && result.Restores.Count == 0 ? "" : Environment.NewLine + Describe(result)));
    }

    /// <summary>Nombre de una presentación para mostrarla (en el idioma de la interfaz).</summary>
    private static string TemplateLabel(PanelTemplate template) => template switch
    {
        PanelTemplate.Stack => Strings.Tpl_Stack,
        PanelTemplate.Fill => Strings.Tpl_Fill,
        PanelTemplate.List => Strings.Tpl_List,
        _ => Strings.Tpl_Tiles,
    };

    /// <summary>En un menú, "_" marca la tecla de acceso: se duplica para que se vea tal cual (Chrome_WidgetWin_1).</summary>
    private static string NoAccessKey(string text) => text.Replace("_", "__", StringComparison.Ordinal);

    // --- Ventanas: acciones de cada fila ---------------------------------------------------------------

    /// <summary>Clic derecho en una ventana de la lista: su menú.</summary>
    private void WindowList_ContextMenuOpening(object sender, WC.ContextMenuEventArgs e)
    {
        var menu = WindowList.ContextMenu!;
        menu.Items.Clear();
        if (WindowList.SelectedItem is not WindowRow row)
        {
            e.Handled = true; // nada seleccionado: no hay menú
            return;
        }

        FillRowMenu(menu, row);
    }

    /// <summary>«…» de una fila: el mismo menú que el clic derecho.</summary>
    private void RowMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WindowRow row } button)
        {
            return;
        }

        WindowList.SelectedItem = row;
        var menu = new WC.ContextMenu { PlacementTarget = button, Placement = WC.Primitives.PlacementMode.Bottom };
        FillRowMenu(menu, row);
        menu.IsOpen = true;
    }

    /// <summary>
    /// Lo que se puede hacer con una ventana: al foco, pantalla completa en un monitor, estacionar, recuperar,
    /// reacomodar o liberar; su tamaño; y excluir su app o su clase de ventana.
    /// </summary>
    private void FillRowMenu(WC.ContextMenu menu, WindowRow row)
    {
        void Item(WC.ItemsControl parent, string header, Func<Task> action)
        {
            var item = new WC.MenuItem { Header = NoAccessKey(header) };
            item.Click += async (_, _) => await action();
            parent.Items.Add(item);
        }

        var h = row.Snapshot.Handle;
        var managed = _controller.Managed().FirstOrDefault(m => m.Handle == h);
        if (row.Manageable)
        {
            Item(menu, Strings.Main_Promote, () => Run(() => _controller.Execute(managed is null
                ? [new Adopt(h, row.App, DefaultPanel()), new Promote(h)]
                : [new Promote(h)])));

            var fullscreen = new WC.MenuItem { Header = NoAccessKey(Strings.DeckMenu_FullscreenOn) };
            var aux = ShellController.AuxiliaryDisplays();
            var targets = new List<(string? Display, string Name)> { (null, Strings.Monitor_Principal) };
            targets.AddRange(aux.Select(a => ((string?)a.Key, a.FriendlyName ?? a.DeviceName)));
            foreach (var (display, name) in targets)
            {
                Item(fullscreen, name, () => Run(() => _controller.Execute(managed switch
                {
                    null => [new Adopt(h, row.App, DefaultPanel()), new EnterFullscreen(h, display)],
                    { Placement: Placement.Fullscreen } when _controller.FullscreenDisplay == display => [new ExitFullscreen(h)],
                    _ => [new EnterFullscreen(h, display)],
                })));
            }

            menu.Items.Add(fullscreen);
            if (managed is not null)
            {
                Item(menu, managed.Placement == Placement.Parked ? Strings.Main_Unpark : Strings.Main_Park, () =>
                    Run(() => _controller.Execute(managed.Placement == Placement.Parked ? new Restore(h) : new Park(h))));
                if (managed.Detached)
                {
                    Item(menu, Strings.Main_Reattach, () => Run(() => _controller.Execute(new Reattach(h))));
                }

                Item(menu, Strings.Main_Release, () => Run(() => _controller.Execute(new Release(h))));
            }

            menu.Items.Add(new WC.Separator());

            // Tamaño: según la ventana, fijo (sólo moverla) o redimensionar siempre.
            var (_, mode, appKey) = _controller.SizeInfo(h);
            if (appKey.Length > 0)
            {
                var size = new WC.MenuItem { Header = NoAccessKey(Core.Text.F(Strings.Menu_SizeOf, row.App)) };
                foreach (var (option, label) in new[]
                {
                    (AppSizeMode.Auto, row.Snapshot.IsResizable ? Strings.Size_AutoResizable : Strings.Size_AutoFixed),
                    (AppSizeMode.Fixed, Strings.Size_Fixed),
                    (AppSizeMode.Resizable, Strings.Size_Resizable),
                })
                {
                    var item = new WC.MenuItem { Header = NoAccessKey(label), IsCheckable = true, IsChecked = mode == option };
                    item.Click += (_, _) =>
                    {
                        var result = _controller.SetAppSizeMode(appKey, option);
                        Refresh();
                        SetStatus(Core.Text.F(Strings.Size_Set, row.App, label.ToLowerInvariant()) + (result.Applied is null ? "" : Environment.NewLine + Describe(result)));
                    };
                    size.Items.Add(item);
                }

                menu.Items.Add(size);
            }
        }

        if (ExclusionRule.ForApp(row.Snapshot) is { } appRule)
        {
            var app = new WC.MenuItem { Header = NoAccessKey(Core.Text.F(Strings.Menu_ExcludeApp, row.App, appRule.Value)) };
            app.Click += (_, _) => AddExclusion(appRule);
            menu.Items.Add(app);
        }

        var classRule = new ExclusionRule(ExclusionKind.WindowClass, row.Snapshot.ClassName);
        var cls = new WC.MenuItem { Header = NoAccessKey(Core.Text.F(Strings.Menu_ExcludeClass, row.Snapshot.ClassName)) };
        cls.Click += (_, _) => AddExclusion(classRule);
        menu.Items.Add(cls);
    }

    // --- Atajos y gestos -----------------------------------------------------------------------------

    /// <summary>Los atajos en uso, uno por fila: la combinación y lo que hace.</summary>
    private void BuildShortcuts()
    {
        var app = (App)Application.Current;
        ShortcutList.Children.Clear();
        ShortcutList.RowDefinitions.Clear();
        ShortcutList.ColumnDefinitions.Clear();
        ShortcutList.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = GridLength.Auto });
        ShortcutList.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        foreach (var action in Core.Input.Hotkeys.Actions.Where(a => !a.Id.StartsWith("escena-", StringComparison.Ordinal)))
        {
            var chord = app.ChordOf(action.Id);
            if (chord.Length == 0)
            {
                continue;
            }

            ShortcutList.RowDefinitions.Add(new WC.RowDefinition { Height = GridLength.Auto });
            var key = new WC.Border
            {
                Child = Text(chord, "TextFillColorPrimaryBrush", 13),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 3, 16, 3),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            key.SetResourceReference(WC.Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            key.SetResourceReference(WC.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            WC.Grid.SetRow(key, row);
            ShortcutList.Children.Add(key);
            var what = Text(action.Name, "TextFillColorSecondaryBrush");
            WC.Grid.SetRow(what, row);
            WC.Grid.SetColumn(what, 1);
            ShortcutList.Children.Add(what);
            row++;
        }
    }

    private void Hotkeys_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeysWindow(_settings) { Owner = this };
        dialog.ShowDialog();
        BuildShortcuts();
        Refresh();
    }

    private void Deck_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ToggleDeck();

    // --- Inicio: disposición del principal y mapa de monitores ----------------------------------------

    internal static string ArrangementName(FocusPosition view) => view switch
    {
        FocusPosition.Right => Strings.Arr_Right,
        FocusPosition.Left => Strings.Arr_Left,
        FocusPosition.ContextOnly => Strings.Arr_ContextOnly,
        FocusPosition.WorkOnly => Strings.Arr_WorkOnly,
        FocusPosition.FocusOnly => Strings.Arr_FocusOnly,
        _ => Strings.Arr_Center,
    };

    /// <summary>
    /// Los interruptores de las columnas del principal, como en el deck (ADR 0012, enmienda): azul, prendida;
    /// en un ultrawide, el extra que trae la otra columna a este lado.
    /// </summary>
    private void BuildLayout(IReadOnlyList<Panel> panels)
    {
        bool ultrawide = ShellController.UltrawideNow();
        var view = _controller.EffectiveView;
        string ColumnName(PanelSide side, string fallback) =>
            panels.Where(p => p.Display is null && p.Side == side).OrderBy(p => p.Slot).FirstOrDefault()?.Name ?? fallback;
        string left = ColumnName(PanelSide.Left, Strings.Deck_LeftColumn);
        string right = ColumnName(PanelSide.Right, Strings.Deck_RightColumn);

        LayoutToggles.Children.Clear();
        void Toggle(string text, string tip, bool on, FocusPosition next)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = text,
                Appearance = on ? ControlAppearance.Primary : ControlAppearance.Secondary,
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 120,
                ToolTip = tip,
            };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.Click += async (_, _) => await Run(() => _controller.Execute(new SetView(next)));
            LayoutToggles.Children.Add(b);
        }

        if (MainArrangement.CanBorrowToLeft(view, ultrawide))
        {
            Toggle("⇠  " + right, Core.Text.F(Strings.Deck_BorrowTip, right), MainArrangement.RightBorrowedLeft(view), MainArrangement.ToggleBorrowToLeft(view));
        }

        Toggle("◧  " + left, Core.Text.F(Strings.Deck_ColumnTip, left), MainArrangement.LeftOn(view), MainArrangement.ToggleLeft(view));
        Toggle(right + "  ◨", Core.Text.F(Strings.Deck_ColumnTip, right), MainArrangement.RightOn(view), MainArrangement.ToggleRight(view));
        if (MainArrangement.CanBorrowToRight(view, ultrawide))
        {
            Toggle(left + "  ⇢", Core.Text.F(Strings.Deck_BorrowTip, left), MainArrangement.LeftBorrowedRight(view), MainArrangement.ToggleBorrowToRight(view));
        }

        ProportionsText.Text = $"{ArrangementName(view)} · {_controller.Proportions.Describe(view)}";
        ResetWidthButton.IsEnabled = view != FocusPosition.FocusOnly;
    }

    private async void ResetWidth_Click(object sender, RoutedEventArgs e)
    {
        var view = _controller.EffectiveView;
        await Run(() => _controller.Execute(new SetProportions(view, null)));
    }

    /// <summary>
    /// El mapa de tus monitores: el principal con sus columnas en el orden y la proporción de la disposición,
    /// y qué hay en el foco; los demás con sus paneles. Cada panel dice cuántas apps tiene.
    /// </summary>
    private void BuildMonitorMap(IReadOnlyList<Panel> panels, IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux)
    {
        MonitorMap.Children.Clear();
        var managed = _controller.Managed();
        var names = _rows.ToDictionary(r => r.Snapshot.Handle, r => r.App);
        int Count(Panel p) => managed.Count(m => m.PanelId == p.Id && m.Placement is not (Placement.Center or Placement.Fullscreen));
        string Lines(IEnumerable<Panel> ps) => string.Join("\n", ps.OrderBy(p => p.Slot).Select(p => Core.Text.F(Strings.Home_PanelLine, p.Name, Count(p))));
        var focus = managed.FirstOrDefault(m => m.Placement is Placement.Center or Placement.Fullscreen);
        string focusName = focus is null ? Strings.Home_FocusEmpty : Core.Text.F(Strings.Home_Focus, names.GetValueOrDefault(focus.Handle, focus.Label));

        WC.Border Column(string text, bool accent, double weight)
        {
            // Una línea por panel, recortada si la columna es estrecha (nunca partida a media palabra).
            var label = new WC.TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 6, 8, 6), ToolTip = text };
            var col = new WC.Border { Child = label, CornerRadius = new CornerRadius(4), Margin = new Thickness(2), Tag = weight };
            if (accent)
            {
                // El foco, con el azul del foco del deck y texto blanco: igual en los dos temas.
                label.Foreground = Brushes.White;
                col.Background = FocusBlue;
            }
            else
            {
                label.SetResourceReference(WC.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                col.SetResourceReference(WC.Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
            }
            return col;
        }

        WC.StackPanel Monitor(string title, double width, IReadOnlyList<WC.Border> columns)
        {
            var grid = new WC.Grid { Height = 110 };
            for (int i = 0; i < columns.Count; i++)
            {
                grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new GridLength((double)columns[i].Tag, GridUnitType.Star) });
                WC.Grid.SetColumn(columns[i], i);
                grid.Children.Add(columns[i]);
            }

            var frame = new WC.Border { Child = grid, Width = width, CornerRadius = new CornerRadius(8), Padding = new Thickness(4), BorderThickness = new Thickness(1) };
            frame.SetResourceReference(WC.Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            frame.SetResourceReference(WC.Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            var block = new WC.StackPanel { Margin = new Thickness(0, 0, 16, 8), VerticalAlignment = VerticalAlignment.Bottom };
            block.Children.Add(frame);
            var caption = Text(title, "TextFillColorSecondaryBrush", 12);
            caption.Margin = new Thickness(2, 4, 0, 0);
            block.Children.Add(caption);
            return block;
        }

        // El principal: sus columnas en el orden de la disposición; las apagadas no salen.
        var principal = ShellController.TargetDisplay();
        var view = _controller.EffectiveView;
        var weights = _controller.Proportions;
        var leftPanels = panels.Where(p => p.Display is null && p.Side == PanelSide.Left).ToList();
        var rightPanels = panels.Where(p => p.Display is null && p.Side == PanelSide.Right).ToList();
        var order = view switch
        {
            FocusPosition.Left => "CRL",
            FocusPosition.Right => "LRC",
            FocusPosition.ContextOnly => "LC",
            FocusPosition.WorkOnly => "CR",
            FocusPosition.FocusOnly => "C",
            _ => "LCR",
        };
        var columns = order.Select(c => c switch
        {
            'L' => Column(Lines(leftPanels), false, weights.Left),
            'R' => Column(Lines(rightPanels), false, weights.Right),
            _ => Column(view == FocusPosition.FocusOnly ? Core.Text.F(Strings.Home_FocusFull, focusName) : focusName, true, weights.Center),
        }).ToList();
        double Ratio(FocusWShell.Core.Displays.DisplayInfo d) => d.WorkArea.Height > 0 ? Math.Clamp((double)d.WorkArea.Width / d.WorkArea.Height, 1, 4) : 16.0 / 9;
        MonitorMap.Children.Add(Monitor(
            $"{Strings.Home_Main} · {principal.FriendlyName ?? principal.DeviceName} · {principal.WorkArea.Width} × {principal.WorkArea.Height}",
            118 * Ratio(principal), columns));

        // Los demás: sus paneles a la izquierda y a la derecha.
        foreach (var d in aux)
        {
            var mine = panels.Where(p => string.Equals(p.Display, d.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var cols = new List<WC.Border>();
            foreach (var side in new[] { PanelSide.Left, PanelSide.Right })
            {
                var ps = mine.Where(p => p.Side == side).ToList();
                if (ps.Count > 0)
                {
                    cols.Add(Column(Lines(ps), false, 1));
                }
            }

            if (cols.Count == 0)
            {
                cols.Add(Column(Strings.Home_NoPanels, false, 1));
            }

            MonitorMap.Children.Add(Monitor(d.FriendlyName ?? d.DeviceName, 118 * Ratio(d), cols));
        }
    }

    private async void Rearrange_Click(object sender, RoutedEventArgs e) =>
        await Run(() => _controller.RearrangeAll() ?? new ControllerResult([], [], null));

    // --- General -------------------------------------------------------------------------------------

    /// <summary>Cambiar de idioma: se guarda y se ofrece reiniciar (los textos se cargan al arrancar).</summary>
    private void LanguageCombo_SelectionChanged(object sender, WC.SelectionChangedEventArgs e)
    {
        if (_loadingGestures || LanguageCombo.SelectedItem is not WC.ComboBoxItem { Tag: string language } || language == _settings.Language)
        {
            return;
        }

        _settings.Language = language;
        _settings.Save();
        if (System.Windows.MessageBox.Show(this, Strings.Main_LanguageRestartAsk, "FocusWShell", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ((App)Application.Current).Restart();
        }
        else
        {
            SetStatus(Strings.Main_LanguageLater);
        }
    }

    /// <summary>
    /// El tema de esta ventana: el de Windows (y lo sigue si cambia con la ventana abierta), claro u oscuro.
    /// El deck tiene su propio aspecto y no cambia.
    /// </summary>
    private void ApplyTheme(string theme)
    {
        Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(this);
        if (theme is "light" or "dark")
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                theme == "dark" ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light, WindowBackdropType.Mica);
        }
        else
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, WC.SelectionChangedEventArgs e)
    {
        if (_loadingGestures || ThemeCombo.SelectedItem is not WC.ComboBoxItem { Tag: string theme } || theme == _settings.Theme)
        {
            return;
        }

        _settings.Theme = theme;
        _settings.Save();
        ApplyTheme(theme);
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingGestures)
        {
            ((App)Application.Current).SetStartWithWindows(StartupSwitch.IsChecked == true);
        }
    }

    private void Awake_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loadingGestures)
        {
            ((App)Application.Current).SetKeepAwakeFromMain(AwakeSwitch.IsChecked == true);
        }
    }

    private static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    private void Manual_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
            ? "https://proteo5.github.io/FocusWShell/es/"
            : "https://proteo5.github.io/FocusWShell/");

    private void Report_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/proteo5/FocusWShell/issues/new/choose");

    private void Releases_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/proteo5/FocusWShell/releases");

    private void Source_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/proteo5/FocusWShell");

    private void License_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/proteo5/FocusWShell/blob/main/LICENSE");

    private void SettingsFolder_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusWShell"));

    private void OpenMissing_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingGestures)
        {
            return;
        }

        _settings.OpenMissingApps = OpenMissingBox.IsChecked == true;
        _settings.Save();
    }

    private void ShowHiddenApps_Changed(object sender, RoutedEventArgs e) => BuildCatalog(_controller.Panels());

    /// <summary>Qué gestos de Windows llevan al foco (ADR 0003, enmienda). Se guarda al momento.</summary>
    private void Gesture_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingGestures)
        {
            return;
        }

        _settings.FocusOnAltTab = GestureAltTab.IsChecked == true;
        _settings.FocusOnTaskView = GestureTaskView.IsChecked == true;
        _settings.FocusOnTaskbar = GestureTaskbar.IsChecked == true;
        _settings.FocusOnClick = GestureClick.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Cerrar la ventana sólo la oculta: el gestor sigue en la bandeja.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!((App)Application.Current).IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    public void ShowAndActivate()
    {
        Refresh();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // Al frente aunque Windows no nos deje tomar el primer plano (p. ej. si se abre desde el deck o la
        // bandeja con el toque de la tablet): un instante "siempre encima", y la activación robusta.
        Topmost = true;
        Activate();
        FocusWShell.Win32.WindowMover.Activate(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Topmost = false;
    }

    /// <summary>Destino que el modelo da a una ventana gestionada.</summary>
    private static string AssignmentOf(ManagedWindow m) =>
        m.Placement == Placement.Center ? AssignmentOption.Focus : m.PanelId;

    private string StateOf(ManagedWindow? m, Dictionary<string, Panel> panels)
    {
        if (m is null)
        {
            return "";
        }

        string name = panels.TryGetValue(m.PanelId, out var p) ? p.Name : m.PanelId;
        string missing = p is not null && _controller.IsDisplayMissing(p.Display) ? Strings.State_MonitorMissing : "";
        return m switch
        {
            { Placement: Placement.Parked } => Core.Text.F(Strings.State_Parked, name, missing),
            { Detached: true } => Strings.State_Detached,
            { Placement: Placement.Center } => Core.Text.F(Strings.State_Focus, name),
            { Placement: Placement.Fullscreen } => Core.Text.F(Strings.State_Fullscreen, name),
            _ => Core.Text.F(Strings.State_In, name, missing),
        };
    }

    /// <summary>Nombre de un monitor para la UI.</summary>
    private static string DisplayName(string? key, IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux) =>
        key is null ? Strings.Monitor_Principal : aux.FirstOrDefault(d => d.Key == key) is { } d ? d.FriendlyName ?? d.DeviceName.TrimStart('\\', '.') : Strings.Monitor_Missing;

    private static string LocationLabel(Panel p, IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux) =>
        p.Display is null
            ? (p.Side == PanelSide.Left ? Strings.Where_PrincipalLeft : Strings.Where_PrincipalRight)
            : Core.Text.F(p.Side == PanelSide.Left ? Strings.Where_AuxLeft : Strings.Where_AuxRight, DisplayName(p.Display, aux));

    public void Refresh()
    {
        var aux = ShellController.AuxiliaryDisplays();

        // Primero se desconectan las filas: al vaciar las opciones, WPF les escribe un valor vacío.
        foreach (var row in _rows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        var panels = _controller.Panels();
        AssignmentOptions.Clear();
        AssignmentOptions.Add(new AssignmentOption(AssignmentOption.Free, Strings.Assign_Free));
        AssignmentOptions.Add(new AssignmentOption(AssignmentOption.Focus, Strings.Assign_Focus));
        foreach (var p in panels)
        {
            AssignmentOptions.Add(new AssignmentOption(p.Id, $"{p.Name} ({LocationLabel(p, aux)})"));
        }

        BuildPanels(panels, aux);
        BuildCatalog(panels);

        var selected = (WindowList.SelectedItem as WindowRow)?.Snapshot.Handle;
        _rows.Clear();
        var windows = _controller.Refresh();
        var managed = _controller.Managed().ToDictionary(m => m.Handle);
        var byId = panels.ToDictionary(p => p.Id);
        System.Windows.Media.ImageSource? IconOf(WindowSnapshot w) => _controller.CatalogAppOf(w) is { } app ? AppIcons.Get(app) : null;
        foreach (var w in windows)
        {
            managed.TryGetValue(w.Handle, out var m);
            var assignment = _pending.TryGetValue(w.Handle, out var p) ? p : m is null ? AssignmentOption.Free : AssignmentOf(m);
            var state = StateOf(m, byId);
            if (m is not null && _controller.SizeInfo(w.Handle).Fixed)
            {
                state += Strings.State_FixedSuffix;
            }

            var row = new WindowRow(w, assignment, _controller.AppName(w)) { State = state, Icon = IconOf(w) };
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        // Las que no se pueden mover (M2), al final y sin elegir panel: así no "desaparecen".
        foreach (var (w, reason) in _controller.UnmanageableWindows())
        {
            _rows.Add(new WindowRow(w, AssignmentOption.Free, _controller.AppName(w))
            {
                State = Core.Text.F(Strings.State_Unmanageable, reason),
                Manageable = false,
                Icon = IconOf(w),
            });
        }

        foreach (var gone in _pending.Keys.Except(windows.Select(w => w.Handle)).ToList())
        {
            _pending.Remove(gone);
        }

        WindowList.SelectedItem = _rows.FirstOrDefault(r => r.Snapshot.Handle == selected);

        // Inicio.
        BuildLayout(panels);
        BuildMonitorMap(panels, aux);
        RearrangeButton.IsEnabled = _controller.CanRearrange;
        var emergency = ((App)Application.Current).ChordOf(Core.Input.Hotkeys.Emergency);
        SafetyInfo.Message = Core.Text.F(Strings.Home_SafetyText, emergency.Length > 0 ? emergency : "Ctrl+Alt+Shift+Backspace");
        if (ShortcutsSection.Visibility == Visibility.Visible)
        {
            BuildShortcuts();
        }
    }

    /// <summary>
    /// Sólo para <c>--main-preview … sample</c>: cambia la lista de Ventanas por filas de ejemplo, para las
    /// capturas del manual sin títulos reales.
    /// </summary>
    internal void ShowSampleWindows(IEnumerable<WindowRow> rows)
    {
        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    /// <summary>Un puesto donde puede ir un panel: monitor, lado y arriba/abajo.</summary>
    private sealed record Location(string? Display, PanelSide Side, PanelSlot Slot, string Label);

    /// <summary>
    /// Sección de paneles (ADR 0011): una fila por panel para renombrarlo, moverlo, cambiar su
    /// presentación o borrarlo, y una fila para crear uno nuevo.
    /// </summary>
    private void BuildPanels(IReadOnlyList<Panel> panels, IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux)
    {
        _buildingPanels = true;
        PanelsList.Children.Clear();

        // Ubicaciones posibles: en el principal y en cada auxiliar conectado, cada columna con dos puestos
        // (arriba y abajo, ADR 0011). Se indica qué panel ocupa cada uno.
        string Occupant(string? display, PanelSide side, PanelSlot slot) =>
            panels.FirstOrDefault(p => p.Side == side && p.Slot == slot && string.Equals(p.Display, display, StringComparison.OrdinalIgnoreCase)) is { } o
                ? $" ({o.Name})"
                : "";
        string Spot(string where, string? display, PanelSide side, PanelSlot slot) =>
            Core.Text.F(slot == PanelSlot.Upper ? Strings.Spot_Upper : Strings.Spot_Lower, where, Occupant(display, side, slot));
        var locations = new List<Location>();
        void AddColumn(string? display, PanelSide side, string where)
        {
            locations.Add(new(display, side, PanelSlot.Upper, Spot(where, display, side, PanelSlot.Upper)));
            locations.Add(new(display, side, PanelSlot.Lower, Spot(where, display, side, PanelSlot.Lower)));
        }

        AddColumn(null, PanelSide.Left, Strings.Col_PrincipalLeft);
        AddColumn(null, PanelSide.Right, Strings.Col_PrincipalRight);
        foreach (var a in aux)
        {
            string name = DisplayName(a.Key, aux);
            AddColumn(a.Key, PanelSide.Left, Core.Text.F(Strings.Col_AuxLeft, name));
            AddColumn(a.Key, PanelSide.Right, Core.Text.F(Strings.Col_AuxRight, name));
        }

        var presentations = new List<(PanelTemplate Template, string Label)>
        {
            (PanelTemplate.Tiles, Strings.Tpl_Tiles),
            (PanelTemplate.Stack, Strings.Tpl_Stack),
            (PanelTemplate.Fill, Strings.Tpl_Fill),
            (PanelTemplate.List, Strings.Tpl_ListLong),
        };

        System.Windows.Controls.ComboBox LocationCombo(string label, string? display, PanelSide side, PanelSlot slot)
        {
            var options = locations.ToList();
            if (display is not null && !options.Any(o => o.Display == display))
            {
                // Un monitor que ahora no está conectado: se muestra su sitio para no perderlo.
                options.Add(new(display, side, slot, Spot(Core.Text.F(side == PanelSide.Left ? Strings.Col_AuxLeft : Strings.Col_AuxRight, DisplayName(display, aux)), display, side, slot)));
            }

            var combo = new System.Windows.Controls.ComboBox
            {
                Width = 330,
                Margin = new Thickness(0, 0, 8, 0),
                ItemsSource = options.Select(o => o.Label).ToList(),
                SelectedIndex = Math.Max(0, options.FindIndex(o => o.Display == display && o.Side == side && o.Slot == slot)),
                Tag = options,
            };
            System.Windows.Automation.AutomationProperties.SetName(combo, label);
            return combo;
        }

        foreach (var panel in panels)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var name = new System.Windows.Controls.TextBox { Text = panel.Name, Width = 180, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(name, Core.Text.F(Strings.Panel_NameName, panel.Name));
            name.LostFocus += async (_, _) =>
            {
                if (name.Text.Trim() != panel.Name && name.Text.Trim().Length > 0)
                {
                    // Los controles sólo se leen en el hilo de la UI: el valor se toma antes de la tarea.
                    var newText = name.Text;
                    await Run(() => _controller.Execute(new RenamePanel(panel.Id, newText)));
                    Refresh();
                }
            };

            var location = LocationCombo(Core.Text.F(Strings.Panel_LocationName, panel.Name), panel.Display, panel.Side, panel.Slot);
            location.SelectionChanged += async (_, _) =>
            {
                if (_buildingPanels || location.Tag is not List<Location> options || location.SelectedIndex < 0)
                {
                    return;
                }

                var (d, s, slot, _) = options[location.SelectedIndex];
                await Run(() => _controller.Execute(new MovePanel(panel.Id, d, s, slot)));
                Refresh();
            };

            var presentation = new System.Windows.Controls.ComboBox
            {
                Width = 150,
                Margin = new Thickness(0, 0, 8, 0),
                ItemsSource = presentations.Select(o => o.Label).ToList(),
                SelectedIndex = Math.Max(0, presentations.FindIndex(o => o.Template == panel.Template)),
            };
            System.Windows.Automation.AutomationProperties.SetName(presentation, Core.Text.F(Strings.Panel_PresentationName, panel.Name));
            presentation.SelectionChanged += async (_, _) =>
            {
                if (_buildingPanels || presentation.SelectedIndex < 0)
                {
                    return;
                }

                var template = presentations[presentation.SelectedIndex].Template;
                await Run(() => _controller.Execute(new SetPresentation(panel.Id, template)));
                Refresh();
            };

            var delete = new Wpf.Ui.Controls.Button { Content = Strings.Main_Delete, Icon = new SymbolIcon(SymbolRegular.Delete24) };
            System.Windows.Automation.AutomationProperties.SetName(delete, Core.Text.F(Strings.Panel_DeleteName, panel.Name));
            delete.Click += async (_, _) =>
            {
                if (System.Windows.MessageBox.Show(this, Core.Text.F(Strings.Panel_DeleteAsk, panel.Name), "FocusWShell",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await Run(() => _controller.Execute(new DeletePanel(panel.Id)));
                    Refresh();
                }
            };

            row.Children.Add(name);
            row.Children.Add(location);
            row.Children.Add(presentation);
            row.Children.Add(delete);
            PanelsList.Children.Add(CardRow(row));
        }

        // Fila para crear un panel nuevo.
        var create = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var newName = new Wpf.Ui.Controls.TextBox { Width = 180, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, PlaceholderText = Strings.Panel_NewName };
        System.Windows.Automation.AutomationProperties.SetName(newName, Strings.Panel_NewName);
        // Para el panel nuevo se propone el primer puesto libre: de un auxiliar si hay, si no del principal.
        var free = locations.Where(o => Occupant(o.Display, o.Side, o.Slot).Length == 0).ToList();
        var suggested = free.FirstOrDefault(o => o.Display is not null) ?? free.FirstOrDefault() ?? locations[0];
        var newLocation = LocationCombo(Strings.Panel_NewLocation, suggested.Display, suggested.Side, suggested.Slot);
        var add = new Wpf.Ui.Controls.Button { Content = Strings.Panel_Create, Icon = new SymbolIcon(SymbolRegular.Add24), Appearance = ControlAppearance.Primary };
        add.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(newName.Text) || newLocation.Tag is not List<Location> options || newLocation.SelectedIndex < 0)
            {
                SetStatus(Strings.Panel_NeedNameAndPlace);
                return;
            }

            var (d, s, slot, _) = options[newLocation.SelectedIndex];
            string id = $"panel-{Guid.NewGuid():N}"[..14];
            var panelName = newName.Text.Trim();
            await Run(() => _controller.Execute(new CreatePanel(new Panel(id, panelName, d, s, PanelTemplate.Tiles, slot))));
            Refresh();
        };
        create.Children.Add(newName);
        create.Children.Add(newLocation);
        create.Children.Add(add);
        PanelsList.Children.Add(CardRow(create));
        _buildingPanels = false;
    }

    /// <summary>
    /// Apps conocidas (ADR 0011): las abiertas en negrita; elegir panel y "Abrir" lanza la app y asigna
    /// su ventana a ese panel. Por defecto se propone el último panel en el que estuvo.
    /// </summary>
    private void BuildCatalog(IReadOnlyList<Panel> panels)
    {
        CatalogList.Children.Clear();
        var apps = _controller.CatalogApps(includeHidden: ShowHiddenApps.IsChecked == true);
        if (apps.Count == 0)
        {
            CatalogList.Children.Add(Text(Strings.Apps_None, "TextFillColorSecondaryBrush"));
            return;
        }

        foreach (var (app, open) in apps)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Opacity = app.Hidden ? 0.6 : 1 };
            row.Children.Add(new System.Windows.Controls.Image
            {
                Source = AppIcons.Get(app),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            // El nombre se edita en el sitio; se guarda al salir del cuadro.
            var name = new System.Windows.Controls.TextBox
            {
                Text = app.Name,
                Width = 200,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = open ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = app.ExecutablePath ?? app.Aumid ?? app.AppKey,
            };
            System.Windows.Automation.AutomationProperties.SetName(name, Core.Text.F(Strings.Apps_NameName, app.Name));
            name.LostFocus += (_, _) =>
            {
                if (name.Text.Trim().Length > 0 && name.Text.Trim() != app.Name)
                {
                    _controller.RenameApp(app.AppKey, name.Text);
                }
            };
            row.Children.Add(name);
            var status = Text(open ? Strings.Apps_Open : Strings.Apps_Closed, "TextFillColorSecondaryBrush");
            status.Width = 80;
            row.Children.Add(status);

            var target = new System.Windows.Controls.ComboBox
            {
                Width = 230,
                Margin = new Thickness(0, 0, 8, 0),
                ItemsSource = panels.Select(p => p.Name).ToList(),
                SelectedIndex = Math.Max(0, panels.ToList().FindIndex(p => p.Id == (app.LastPanelId ?? DefaultPanel()))),
            };
            System.Windows.Automation.AutomationProperties.SetName(target, Core.Text.F(Strings.Apps_TargetName, app.Name));

            var launch = new Wpf.Ui.Controls.Button
            {
                Content = Strings.Apps_Launch,
                Icon = new SymbolIcon(SymbolRegular.Open24),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = app.CanLaunch,
                ToolTip = app.CanLaunch ? Strings.Apps_LaunchTip : Strings.Apps_CantLaunch,
            };
            System.Windows.Automation.AutomationProperties.SetName(launch, Core.Text.F(Strings.Apps_LaunchName, app.Name));
            launch.Click += async (_, _) =>
            {
                if (target.SelectedIndex < 0)
                {
                    return;
                }

                var panelId = panels[target.SelectedIndex].Id;
                var key = app.AppKey;
                launch.IsEnabled = false;
                SetStatus(Core.Text.F(Strings.Apps_Opening, app.Name));
                SetStatus(await Task.Run(() => _controller.OpenApp(key, panelId)));
                Refresh();
            };

            var hide = new Wpf.Ui.Controls.Button
            {
                Content = app.Hidden ? Strings.Apps_Show : Strings.Apps_Hide,
                Icon = new SymbolIcon(app.Hidden ? SymbolRegular.Eye24 : SymbolRegular.EyeOff24),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = app.Hidden ? Strings.Apps_ShowTip : Strings.Apps_HideTip,
            };
            System.Windows.Automation.AutomationProperties.SetName(hide, Core.Text.F(app.Hidden ? Strings.Apps_ShowName : Strings.Apps_HideName, app.Name));
            hide.Click += (_, _) => _controller.SetAppHidden(app.AppKey, !app.Hidden);

            var forget = new Wpf.Ui.Controls.Button { Content = Strings.Apps_Forget, Icon = new SymbolIcon(SymbolRegular.Delete24) };
            System.Windows.Automation.AutomationProperties.SetName(forget, Core.Text.F(Strings.Apps_ForgetName, app.Name));
            forget.Click += (_, _) => _controller.ForgetApp(app.AppKey);

            row.Children.Add(target);
            row.Children.Add(launch);
            row.Children.Add(hide);
            row.Children.Add(forget);
            CatalogList.Children.Add(CardRow(row));
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WindowRow row || e.PropertyName != nameof(WindowRow.Assignment) || row.Assignment is null)
        {
            return;
        }

        _pending[row.Snapshot.Handle] = row.Assignment;
        Dispatcher.BeginInvoke(ApplyPending);
    }

    /// <summary>Aplica los paneles elegidos en la lista. Una vez a la vez: un cambio durante otro espera.</summary>
    private async void ApplyPending()
    {
        if (_applying)
        {
            return;
        }

        var commands = PendingCommands();
        _pending.Clear();
        if (commands.Count == 0)
        {
            return;
        }

        _applying = true;
        try
        {
            await Run(() => _controller.Execute(commands));
        }
        finally
        {
            _applying = false;
        }
    }

    private bool _applying;

    /// <summary>Panel por defecto para una ventana que va al foco sin tener panel: el derecho del principal.</summary>
    private string DefaultPanel() =>
        _controller.Panels().FirstOrDefault(p => p.Display is null && p.Side == PanelSide.Right)?.Id
        ?? _controller.Panels().First(p => p.Display is null).Id;

    /// <summary>Traduce los destinos editados a comandos: liberar, adoptar o cambiar de panel, y al foco al final.</summary>
    private List<Command> PendingCommands()
    {
        var managed = _controller.Managed().ToDictionary(m => m.Handle);
        var releases = new List<Command>();
        var moves = new List<Command>();
        Command? promote = null;
        foreach (var row in _rows)
        {
            var h = row.Snapshot.Handle;
            managed.TryGetValue(h, out var m);
            var current = m is null ? AssignmentOption.Free : AssignmentOf(m);
            if (row.Assignment is null || row.Assignment == current)
            {
                continue;
            }

            switch (row.Assignment)
            {
                case AssignmentOption.Free:
                    releases.Add(new Release(h));
                    break;
                case AssignmentOption.Focus:
                    if (m is null)
                    {
                        moves.Add(new Adopt(h, row.App, DefaultPanel()));
                    }

                    promote = new Promote(h);
                    break;
                default:
                    if (m is null)
                    {
                        moves.Add(new Adopt(h, row.App, row.Assignment));
                    }
                    else
                    {
                        if (m.Placement == Placement.Center)
                        {
                            moves.Add(new ReturnToPanel(h));
                        }

                        moves.Add(new SendTo(h, row.Assignment));
                    }

                    break;
            }
        }

        if (promote is not null)
        {
            moves.Add(promote);
        }

        return [.. releases, .. moves];
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _pending.Clear();
        Refresh();
        SetStatus(Strings.Report_Done);
    }

    private async void Swap_Click(object sender, RoutedEventArgs e) => await Run(() => _controller.Execute(new SwapWithPrevious()));

    private async void Return_Click(object sender, RoutedEventArgs e) => await Run(_controller.ReturnCenter);

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        _pending.Clear();
        await Run(_controller.FreeAll);
    }

    /// <summary>Ejecuta en segundo plano, avisa del resultado y pone la ventana al día.</summary>
    private async Task Run(Func<ControllerResult> action)
    {
        var result = await Task.Run(action);
        SetStatus(Describe(result), result.Rejections.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        Refresh();
    }

    /// <summary>Un aviso en la franja de arriba, que se cierra sola.</summary>
    public void SetStatus(string text) => SetStatus(text, InfoBarSeverity.Informational);

    private void SetStatus(string text, InfoBarSeverity severity)
    {
        StatusBar.Message = text.Trim();
        StatusBar.Severity = severity;
        StatusBar.IsOpen = text.Trim().Length > 0;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void LoadScenes()
    {
        var selected = SceneCombo.SelectedItem as string;
        var names = _controller.SceneNames();
        SceneCombo.ItemsSource = names.Select((n, i) => i < 9 ? $"{i + 1}. {n}" : n).ToList();
        SceneCombo.Tag = names;
        SceneCombo.SelectedIndex = selected is null ? (names.Count > 0 ? 0 : -1) : Math.Max(0, SceneCombo.Items.IndexOf(selected));
    }

    private string? SelectedScene() =>
        SceneCombo.Tag is IReadOnlyList<string> names && SceneCombo.SelectedIndex >= 0 && SceneCombo.SelectedIndex < names.Count
            ? names[SceneCombo.SelectedIndex]
            : null;

    private async void ApplyScene_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedScene() is not { } name)
        {
            SetStatus(Strings.Scene_None);
            return;
        }

        _pending.Clear();
        var result = await Task.Run(() => _controller.ApplySavedScene(name));
        Refresh();
        SetStatus(result.Summary + Environment.NewLine + Describe(result.Result));
    }

    private void SaveScene_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(SceneName.Text) ? SelectedScene() : SceneName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus(Strings.Scene_NeedName);
            return;
        }

        var scene = _controller.SaveScene(name);
        SceneName.Text = "";
        SetStatus(Core.Text.F(Strings.Scene_Saved, scene.Name, scene.Windows.Count, scene.Templates.Count));
        LoadScenes();
        SceneCombo.SelectedIndex = Math.Max(0, (SceneCombo.Tag as IReadOnlyList<string>)?.ToList().FindIndex(n => n == scene.Name) ?? 0);
    }

    private void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedScene() is not { } name)
        {
            return;
        }

        if (System.Windows.MessageBox.Show(this, Core.Text.F(Strings.Scene_DeleteAsk, name), "FocusWShell", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes)
        {
            _controller.DeleteScene(name);
            SetStatus(Core.Text.F(Strings.Scene_Deleted, name));
        }
    }

    public static string Describe(ControllerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var sb = new StringBuilder();
        foreach (var r in result.Rejections)
        {
            sb.AppendLine(Core.Text.F(Strings.Report_NotDone, r));
        }

        foreach (var restore in result.Restores.Where(r => r.Entries.Count > 0))
        {
            sb.Append(Describe(restore));
        }

        if (result.Applied is { } report)
        {
            sb.Append(Describe(report));
        }

        return sb.Length == 0 ? Strings.Report_Done : sb.ToString();
    }

    private static string Describe(SceneApplyReport report)
    {
        var sb = new StringBuilder();
        if (report.Error is not null)
        {
            sb.AppendLine(report.Error);
            return sb.ToString();
        }

        foreach (var note in report.Layout.Notes)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"· {note}");
        }

        foreach (var (target, result) in report.Applied.Where(a => a.Result is { IsExact: false }))
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"! {target.Label}: {result!.Error ?? Core.Text.F(Strings.Report_Deviation, result.MaxDeviation)}");
        }

        if (report.CenterInvaders is { Count: > 0 } invaders)
        {
            sb.AppendLine(Core.Text.F(Strings.Report_CenterInvaded, string.Join(", ", invaders)));
        }

        return sb.ToString();
    }

    public static string Describe(RestoreReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine(Core.Text.F(Strings.Report_Restored, report.Summary));
        foreach (var e in report.Entries.Where(e => e.Outcome is RestoreOutcome.Pending or RestoreOutcome.RestoredWithDifferences))
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"  ! {e.Entry.ProcessName}: {e.Detail}");
        }

        return sb.ToString();
    }
}
