using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Win32;

namespace FocusWShell.App;

/// <summary>Lo que el deck necesita de la app para actuar.</summary>
public sealed record DeckActions(
    Func<Command[], Task> Run,
    Func<string, string, Task> OpenApp,
    Action<string> ApplyScene,
    Action ToggleKeepAwake,
    Func<bool> KeepAwake,
    Action RestoreAll,
    Action Hide,
    Action<DeckDock?> Dock,
    Func<DeckDock?> CurrentDock,
    Func<nint, Task> CloseApp);

/// <summary>
/// El deck (ADR 0013, fase 2): una superficie de control en una esquina del monitor principal. Muestra el
/// foco, las vistas, cada panel con sus apps (abiertas a color, cerradas apagadas) y las ventanas libres;
/// tocar una app la lleva al foco (o la abre) y el deck se oculta solo. No roba el foco (S8), y sus botones
/// miden al menos 48 px para el dedo.
/// </summary>
public sealed class DeckWindow : Window
{
    private static readonly Brush Background0 = Frozen(Color.FromRgb(0x14, 0x17, 0x1E));
    private static readonly Brush CardBrush = Frozen(Color.FromRgb(0x24, 0x29, 0x33));
    private static readonly Brush ActiveBrush = Frozen(Color.FromRgb(0x28, 0x78, 0xDC));
    private static readonly Brush WarnBrush = Frozen(Color.FromRgb(0xC2, 0x3B, 0x3B));
    private static readonly Brush AwakeBrush = Frozen(Color.FromRgb(0xC8, 0x8A, 0x1E));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE9, 0xEF));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x8A, 0x93, 0xA2));

    private const double Touch = 48;

    private readonly DeckActions _actions;
    private readonly Grid _root = new() { Margin = new Thickness(10, 10, 2, 2), Background = Background0 };
    private DeckState? _state;

    /// <summary>"Restaurar todas" pide un segundo toque antes de actuar.</summary>
    private DateTime _restoreArmed = DateTime.MinValue;

    public DeckWindow(DeckActions actions)
    {
        _actions = actions;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Background = Background0;
        Title = "FocusWShell · deck";
        Content = _root; // sin scroll: todo cabe (ver Render)
        SizeChanged += (_, _) =>
        {
            if (_state is not null && _renderedWide != IsWide)
            {
                Render();
            }
        };
        SourceInitialized += (_, _) => OwnWindows.MakeToolNoActivate(new WindowInteropHelper(this).Handle);

        // Al ocultarse, se olvida si se estaba moviendo una app: al volver, el deck empieza limpio.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _moving = null;
                _moveApp = null;
                _closing = false;
            }
        };
    }

    /// <summary>Lo muestra (o lo actualiza) en el rectángulo dado, en píxeles físicos.</summary>
    public void Present(DeckState state, PixelRect where)
    {
        _state = state;
        Render();
        if (!IsVisible)
        {
            Show();
        }

        OwnWindows.Place(new WindowInteropHelper(this).EnsureHandle(), where);
    }

    /// <summary>Actualiza el contenido si está visible.</summary>
    public void Update(DeckState state)
    {
        _state = state;
        if (IsVisible)
        {
            Render();
        }
    }

    /// <summary>A partir de este ancho (en DIP) el deck usa dos columnas: controles a la izquierda y paneles a la derecha.</summary>
    private const double WideFrom = 900;

    private bool? _renderedWide;

    private bool IsWide => ActualWidth >= WideFrom;

    /// <summary>
    /// Sin scroll: todo cabe en el deck. Ancho (la tablet entera): controles a la izquierda y paneles en
    /// columnas a la derecha, con las libres abajo. Estrecho (media tablet, esquina del principal): filas
    /// compactas. Las apps de cada panel se encogen para caber (<see cref="FitGrid"/>).
    /// </summary>
    private void Render()
    {
        var state = _state!;
        DropStaleMove(state);
        _renderedWide = IsWide;
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        if (IsWide)
        {
            RenderWide(state);
        }
        else
        {
            RenderNarrow(state);
        }
    }

    /// <summary>Si la app elegida para mover ya no está (se cerró), se vuelve a elegir.</summary>
    private void DropStaleMove(DeckState state)
    {
        if (_moveApp is { Handle: { } chosen } && !state.Panels.SelectMany(p => p.Apps).Concat(state.Free).Any(a => a.Handle == chosen))
        {
            _moveApp = null;
            _moving = MoveStep.PickApp;
        }
    }

    private void RenderWide(DeckState state)
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Izquierda: foco, acciones, vista, ancho, escenas y, abajo, pantalla y restaurar.
        var left = new Grid();
        void Row(UIElement e, GridLength height)
        {
            left.RowDefinitions.Add(new RowDefinition { Height = height });
            Grid.SetRow(e, left.RowDefinitions.Count - 1);
            left.Children.Add(e);
        }

        Row(FocusHeader(state), GridLength.Auto);
        Row(Buttons(2, FocusActions(state, shortText: false)), GridLength.Auto);
        Row(Label("VISTA"), GridLength.Auto);
        Row(Buttons(3, ViewButtons(state, shortText: false)), GridLength.Auto);
        Row(Buttons(3, WidthButtons(state)), GridLength.Auto);
        if (state.Scenes.Count > 0)
        {
            Row(Label("ESCENAS"), GridLength.Auto);
            Row(TextGrid(SceneButtons(state)), new GridLength(1, GridUnitType.Star));
        }
        else
        {
            Row(new Border(), new GridLength(1, GridUnitType.Star));
        }

        var tabs = MonitorTabs(state);
        if (tabs.Count > 1)
        {
            Row(Label("MONITORES"), GridLength.Auto);
            var tabGrid = new UniformGrid { Columns = 1, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var tab in tabs)
            {
                tabGrid.Children.Add(TabButton(tab));
            }

            Row(tabGrid, GridLength.Auto);
        }

        Row(Buttons(2, [AwakeButton(), RestoreButton()]), GridLength.Auto);
        Grid.SetColumn(left, 0);
        _root.Children.Add(left);

        // Derecha: paneles en columnas (dos filas si son más de tres) y las libres abajo.
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(state.Free.Count > 0 ? 3 : 1, GridUnitType.Star) });
        if (state.Free.Count > 0)
        {
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 90 });
        }

        var shown = PanelsOnTab(state);
        var panels = new Grid();
        int perRow = shown.Count <= 3 ? Math.Max(1, shown.Count) : (int)Math.Ceiling(shown.Count / 2.0);
        int rows = (int)Math.Ceiling(shown.Count / (double)perRow);
        for (int c = 0; c < perRow; c++)
        {
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int r = 0; r < Math.Max(1, rows); r++)
        {
            panels.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        if (shown.Count == 0)
        {
            panels.Children.Add(NoPanelsCard(state));
        }

        for (int i = 0; i < shown.Count; i++)
        {
            var card = PanelCard(shown[i]);
            Grid.SetColumn(card, i % perRow);
            Grid.SetRow(card, i / perRow);
            panels.Children.Add(card);
        }

        right.Children.Add(panels);
        if (state.Free.Count > 0)
        {
            var free = FreeCard(state);
            Grid.SetRow(free, 1);
            right.Children.Add(free);
        }

        Grid.SetColumn(right, 2);
        _root.Children.Add(right);
    }

    private void RenderNarrow(DeckState state)
    {
        void Row(UIElement e, GridLength height, double min = 0)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = height, MinHeight = min });
            Grid.SetRow(e, _root.RowDefinitions.Count - 1);
            _root.Children.Add(e);
        }

        Row(FocusHeader(state), GridLength.Auto);
        Row(Buttons(4, FocusActions(state, shortText: true)), GridLength.Auto);
        // Vista y ancho en una sola fila de seis.
        Row(Buttons(6, [.. ViewButtons(state, shortText: true), .. WidthButtons(state)]), GridLength.Auto);

        // Pestañas por monitor y, debajo, sus paneles uno debajo de otro, con alto según cuántas apps tienen.
        var tabs = MonitorTabs(state);
        if (tabs.Count > 1)
        {
            var tabRow = new UniformGrid { Columns = tabs.Count, Margin = new Thickness(0, 0, 0, 2) };
            foreach (var tab in tabs)
            {
                tabRow.Children.Add(TabButton(tab));
            }

            Row(tabRow, GridLength.Auto);
        }

        var shown = PanelsOnTab(state);
        if (shown.Count == 0)
        {
            Row(NoPanelsCard(state), new GridLength(1, GridUnitType.Star), 116);
        }

        foreach (var panel in shown)
        {
            Row(PanelCard(panel), new GridLength(1 + panel.Apps.Count, GridUnitType.Star), 116);
        }

        if (state.Free.Count > 0)
        {
            Row(FreeCard(state), new GridLength(1 + (state.Free.Count / 2.0), GridUnitType.Star), 90);
        }

        // Abajo: escenas, pantalla encendida y restaurar.
        int buttons = state.Scenes.Count + 2;
        Row(TextGrid([.. SceneButtons(state), AwakeButton(), RestoreButton()]), new GridLength(buttons > 3 ? (2 * Touch) + 6 : Touch));
    }

    // --- Pestañas por monitor -------------------------------------------------------------------------

    /// <summary>Un monitor como pestaña del deck.</summary>
    /// <param name="Display">Su clave; <c>null</c>: el principal.</param>
    /// <param name="Apps">Apps abiertas en sus paneles.</param>
    private sealed record MonitorTab(string? Display, string Name, int Apps, bool Connected);

    /// <summary>Pestaña elegida en esta sesión: <c>""</c> el principal; <c>null</c>, ninguna aún (va el principal).</summary>
    private string? _tab;

    private static string TabKey(string? display) => display ?? "";

    /// <summary>Para la vista previa: «mover» (con la primera app de Trabajo elegida) o «cerrar».</summary>
    internal void ShowMode(string? mode, DeckState state)
    {
        if (mode == "mover")
        {
            _moving = MoveStep.PickPanel;
            _moveApp = state.Panels.SelectMany(p => p.Apps).FirstOrDefault(a => a.PanelId == "trabajo" && a.Handle is not null);
        }

        _closing = mode == "cerrar";
    }

    /// <summary>Elige la pestaña de un monitor (<c>null</c>: el principal). Para la vista previa.</summary>
    internal void ShowTab(string? display) => _tab = TabKey(display);

    private bool OnTab(string? display) => string.Equals(TabKey(display), _tab ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Los monitores conectados y los que tienen paneles aunque se hayan desconectado. No sale el monitor
    /// donde el deck ocupa toda la pantalla: ahí no caben paneles.
    /// </summary>
    private List<MonitorTab> MonitorTabs(DeckState state)
    {
        int OpenApps(string? display) => state.Panels
            .Where(p => string.Equals(TabKey(p.Display), TabKey(display), StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Apps.Count(a => a.Handle is not null));
        var whole = _actions.CurrentDock() is { Zone: DeckZone.Whole } dock ? dock.Display : null;
        var tabs = state.Displays
            .Where(d => d.Display is null || !string.Equals(d.Display, whole, StringComparison.OrdinalIgnoreCase))
            .Select(d => new MonitorTab(d.Display, d.Name, OpenApps(d.Display), true))
            .ToList();
        foreach (var missing in state.Panels.Select(p => p.Display).Where(d => d is not null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!tabs.Any(t => string.Equals(TabKey(t.Display), TabKey(missing), StringComparison.OrdinalIgnoreCase)))
            {
                var name = state.Panels.First(p => string.Equals(p.Display, missing, StringComparison.OrdinalIgnoreCase)).Where.Split(',')[0];
                tabs.Add(new MonitorTab(missing, name, OpenApps(missing), false));
            }
        }

        // Si la pestaña elegida ya no existe (p. ej. el deck pasó a ocupar ese monitor), vuelve el principal.
        if (_tab is not null && !tabs.Any(t => OnTab(t.Display)))
        {
            _tab = null;
        }

        return tabs;
    }

    /// <summary>Los paneles del monitor de la pestaña elegida.</summary>
    private List<DeckPanel> PanelsOnTab(DeckState state)
    {
        MonitorTabs(state); // corrige la pestaña si ya no existe
        return state.Panels.Where(p => OnTab(p.Display)).ToList();
    }

    private Button TabButton(MonitorTab tab)
    {
        string text = tab.Connected ? $"{tab.Name} · {tab.Apps}" : $"{tab.Name} (desconectado)";
        var b = WideButton("🖵  " + text, true, () =>
        {
            _tab = TabKey(tab.Display);
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: monitor {tab.Name}");
        if (OnTab(tab.Display))
        {
            b.Background = ActiveBrush;
        }
        else if (!tab.Connected)
        {
            b.Foreground = DimBrush;
        }

        // Con el ratón: al pasar una app arrastrada por encima, se cambia a ese monitor para soltarla allí.
        b.AllowDrop = true;
        b.DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DragFormat) && !OnTab(tab.Display))
            {
                _tab = TabKey(tab.Display);
                Dispatcher.BeginInvoke(Render);
            }
        };
        return b;
    }

    /// <summary>Un monitor sin paneles: cómo crearlos.</summary>
    private Border NoPanelsCard(DeckState state)
    {
        var name = MonitorTabs(state).FirstOrDefault(t => OnTab(t.Display))?.Name ?? "Este monitor";
        return new Border
        {
            BorderBrush = CardBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 8, 8),
            Child = new TextBlock
            {
                Text = $"{name} no tiene paneles. Créalos en la ventana de FocusWShell, pestaña Paneles: escribe un nombre, elige «{name}, izquierda» o «derecha» y pulsa Crear panel.",
                Foreground = DimBrush,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    // --- Piezas ----------------------------------------------------------------------------------------

    private DockPanel FocusHeader(DeckState state)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 8, 8) };
        var close = SmallButton("✕", "Ocultar el deck", () => _actions.Hide());
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var send = SmallButton("📌", "Mandar el deck a", () => { });
        send.Margin = new Thickness(0, 0, 6, 0);
        send.ContextMenu = SendMenu(state);
        send.Click += (_, _) =>
        {
            send.ContextMenu.PlacementTarget = send;
            send.ContextMenu.IsOpen = true;
        };
        DockPanel.SetDock(send, Dock.Right);
        header.Children.Add(send);
        var focus = new StackPanel();
        focus.Children.Add(Label("FOCO"));

        // El icono de la app del foco, a la izquierda de su nombre y su título.
        var line = new DockPanel();
        if (state.Focus?.App is { } focusApp && AppIcons.Get(focusApp) is { } icon)
        {
            var image = new Image
            {
                Source = icon,
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(image, Dock.Left);
            line.Children.Add(image);
        }

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = state.Focus is { } f ? f.Name : "vacío",
            Foreground = state.Focus is null ? DimBrush : TextBrush,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (state.Focus is { Title.Length: > 0 } ft)
        {
            names.Children.Add(new TextBlock { Text = ft.Title, Foreground = DimBrush, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        line.Children.Add(names);
        focus.Children.Add(line);

        header.Children.Add(focus);
        return header;
    }

    private List<Button> FocusActions(DeckState state, bool shortText)
    {
        var previous = WideButton("⇄  Anterior", state.HasPrevious, () => Run(true, new SwapWithPrevious()));
        var back = WideButton(shortText ? "↓  Al panel" : "↓  Foco al panel", state.Focus is not null, () => Run(false, new ReturnToPanel(state.Focus!.Handle!.Value)));
        System.Windows.Automation.AutomationProperties.SetName(back, "Deck: Foco al panel");
        var move = WideButton(_moving is not null ? "⇆  Moviendo…" : shortText ? "⇆  Mover" : "⇆  Mover una app", true, () =>
        {
            _moving = _moving is null ? MoveStep.PickApp : null;
            _moveApp = null;
            _closing = false;
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(move, "Deck: Mover una app");
        if (_moving is not null)
        {
            move.Background = ActiveBrush;
        }

        var close = WideButton(_closing ? "✕  Cerrando…" : shortText ? "✕  Cerrar" : "✕  Cerrar una app", true, () =>
        {
            _closing = !_closing;
            _moving = null;
            _moveApp = null;
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(close, "Deck: Cerrar una app");
        if (_closing)
        {
            close.Background = WarnBrush;
        }

        return [previous, back, move, close];
    }

    /// <summary>
    /// Modo cerrar: la app que se toque se cierra como con su X (si tiene algo sin guardar, ella pregunta) y
    /// el modo se apaga, como «Mover». También se apaga tocando otra vez el botón.
    /// </summary>
    private bool _closing;

    /// <summary>La app que se acaba de mandar cerrar: su tarjeta dice «Cerrando…» hasta que desaparece.</summary>
    private nint? _closingNow;

    private List<Button> ViewButtons(DeckState state, bool shortText)
    {
        var views = shortText
            ? new[] { (FocusPosition.Left, "◧  Izq."), (FocusPosition.Center, "▣  Centro"), (FocusPosition.Right, "◨  Der.") }
            : new[] { (FocusPosition.Left, "◧  Izquierda"), (FocusPosition.Center, "▣  Centro"), (FocusPosition.Right, "◨  Derecha") };
        return views.Select(v =>
        {
            var b = WideButton(v.Item2, true, () => Run(false, new SetView(v.Item1)));
            if (state.View == v.Item1)
            {
                b.Background = ActiveBrush;
            }

            return b;
        }).ToList();
    }

    private List<Button> WidthButtons(DeckState state) =>
        ViewProportions.Presets(state.View).Select(p =>
        {
            var b = WideButton(p.Name, true, () => Run(false, new SetProportions(state.View, p.Weights)));
            if (p.Weights.SameAs(state.Weights))
            {
                b.Background = ActiveBrush;
            }

            return b;
        }).ToList();

    private List<Button> SceneButtons(DeckState state) =>
        state.Scenes.Select(name => WideButton(name, true, () =>
        {
            _actions.ApplyScene(name);
            if (!Docked)
            {
                _actions.Hide();
            }
        })).ToList();

    private Button AwakeButton()
    {
        var awake = WideButton(_actions.KeepAwake() ? "☀  Pantalla encendida: sí" : "☾  Pantalla encendida: no", true, () =>
        {
            _actions.ToggleKeepAwake();
            Render();
        });
        if (_actions.KeepAwake())
        {
            awake.Background = AwakeBrush;
        }

        return awake;
    }

    private Button RestoreButton()
    {
        bool armed = DateTime.Now - _restoreArmed < TimeSpan.FromSeconds(4);
        var restore = WideButton(armed ? "⟲  ¿Seguro? Otra vez" : "⟲  Restaurar todas", true, () =>
        {
            if (DateTime.Now - _restoreArmed < TimeSpan.FromSeconds(4))
            {
                _restoreArmed = DateTime.MinValue;
                _actions.RestoreAll();
                if (!Docked)
                {
                    _actions.Hide();
                }
            }
            else
            {
                _restoreArmed = DateTime.Now;
                Render();
            }
        });
        if (armed)
        {
            restore.Background = WarnBrush;
        }

        return restore;
    }

    /// <summary>Botones en una fila de columnas iguales que se estiran (sin anchos fijos).</summary>
    private static UniformGrid Buttons(int columns, IEnumerable<Button> buttons)
    {
        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var b in buttons)
        {
            StretchOut(b);
            grid.Children.Add(b);
        }

        return grid;
    }

    /// <summary>Botones de texto que se reparten el espacio que haya (escenas, la fila de abajo).</summary>
    private static FitGrid TextGrid(IEnumerable<Button> buttons)
    {
        var grid = new FitGrid { MinCellWidth = 120, MaxCellWidth = 400, Aspect = 4, MinCellHeight = Touch, MaxCellHeight = Touch, Gap = 6, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var b in buttons)
        {
            StretchOut(b);
            b.Margin = new Thickness(0);
            grid.Children.Add(b);
        }

        return grid;
    }

    private static void StretchOut(Button b)
    {
        b.MinWidth = 0;
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Padding = new Thickness(6, 0, 6, 0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Un panel: su cabecera y sus apps, que se encogen para caber. También es zona donde soltar.</summary>
    private Border PanelCard(DeckPanel panel)
    {
        var body = new DockPanel();
        var header = PanelHeader(panel);
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        if (panel.Apps.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "sin apps (suelta aquí una app)", Foreground = DimBrush, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Top });
        }
        else
        {
            body.Children.Add(AppGrid(panel.Apps));
        }

        // Al mover, los paneles donde puede ir la app elegida se ponen amarillos y todo el panel es el
        // destino: un toque en cualquier parte la lleva ahí (más fácil con el dedo que un botón).
        bool target = _moving == MoveStep.PickPanel && _moveApp is { } chosen && chosen.PanelId != panel.Id;
        UIElement content = body;
        if (target)
        {
            var overlay = new Border
            {
                Background = TargetWash,
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "⬇  Mover aquí",
                    Foreground = TextBrush,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            System.Windows.Automation.AutomationProperties.SetName(overlay, $"Deck: Mover {_moveApp!.Name} a {panel.Name}");
            overlay.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await MoveChosenTo(panel);
            };
            var layered = new Grid();
            layered.Children.Add(body);
            layered.Children.Add(overlay);
            content = layered;
        }

        var zone = DropZone(content, panel);
        zone.Margin = new Thickness(0, 0, 8, 8);
        zone.BorderBrush = target ? TargetBrush : CardBrush;
        zone.BorderThickness = new Thickness(target ? 3 : 1);
        zone.Padding = new Thickness(target ? 4 : 6);
        return zone;
    }

    /// <summary>Borde de un panel donde se puede soltar la app que se está moviendo.</summary>
    private static readonly Brush TargetBrush = Frozen(Color.FromRgb(0xF2, 0xC1, 0x2E));

    /// <summary>Velo encima de ese panel: se ve que todo él es el destino.</summary>
    private static readonly Brush TargetWash = Frozen(Color.FromArgb(0x55, 0xF2, 0xC1, 0x2E));

    private Border FreeCard(DeckState state)
    {
        var body = new DockPanel();
        var label = Label("LIBRES");
        DockPanel.SetDock(label, Dock.Top);
        body.Children.Add(label);
        body.Children.Add(AppGrid(state.Free));
        return new Border
        {
            Child = body,
            BorderBrush = CardBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 8, 8),
        };
    }

    /// <summary>Las apps de un panel o las libres: tantas como quepan; si no, la última celda es «+N más».</summary>
    private FitGrid AppGrid(IReadOnlyList<DeckApp> apps)
    {
        var grid = new FitGrid { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var app in apps)
        {
            grid.Children.Add(AppTile(app));
        }

        var more = new Button
        {
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = TextBrush,
            Background = CardBrush,
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        System.Windows.Automation.AutomationProperties.SetName(more, "Deck: más apps");
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more };
            foreach (var hidden in apps.Skip(grid.HiddenFrom))
            {
                var item = new MenuItem { Header = hidden.Name, IsEnabled = (_moving is null && !_closing) || hidden.Handle is not null };
                item.Click += async (_, _) => await TapApp(hidden);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        grid.More = more;
        grid.OverflowChanged = hidden => more.Content = $"+{hidden} más";
        grid.Children.Add(more);
        return grid;
    }

    /// <summary>
    /// Una app: tocarla la lleva al foco (si está cerrada, la abre en su panel; si es libre, la adopta y la
    /// lleva al foco). Clic derecho o pulsación larga: su menú.
    /// </summary>
    private Button AppTile(DeckApp app)
    {
        // El icono crece con la celda (hasta 32 px); el nombre, siempre del mismo tamaño y recortado.
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new Image
        {
            Source = app.App is { } catalog ? AppIcons.Get(catalog) : null,
            MaxWidth = 32,
            MaxHeight = 32,
            MinHeight = 14,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Opacity = app.State == ListItemState.Closed ? 0.45 : 1,
            Margin = new Thickness(0, 1, 0, 2),
        });
        var name = new TextBlock
        {
            Text = app.Handle is { } shown && shown == _closingNow ? "Cerrando…" : app.Name + app.State switch
            {
                ListItemState.InFocus => " ↑",
                ListItemState.Parked => " ⏸",
                ListItemState.Detached => " ↗",
                _ => "",
            },
            Foreground = app.State == ListItemState.Closed ? DimBrush : TextBrush,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        Grid.SetRow(name, 1);
        content.Children.Add(name);

        var tile = new Button
        {
            // El tamaño lo pone la rejilla.
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(4),
            Focusable = false,
            BorderThickness = new Thickness(0),
            Background = app.State == ListItemState.InFocus ? ActiveBrush : CardBrush,
            Cursor = Cursors.Hand,
            ToolTip = string.IsNullOrEmpty(app.Title) ? app.Name : $"{app.Name} · {app.Title}",
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"Deck: {app.Name} ({app.State})");
        if (_moveApp is { } selected && selected.Handle == app.Handle && app.Handle is not null)
        {
            tile.Background = ActiveBrush;
            tile.BorderBrush = TextBrush;
            tile.BorderThickness = new Thickness(2);
        }
        else if ((_moving is not null || _closing) && app.Handle is null)
        {
            tile.Opacity = 0.4; // una app cerrada no se mueve ni se cierra
        }
        else if (_closing)
        {
            tile.BorderBrush = WarnBrush;
            tile.BorderThickness = new Thickness(2);
        }

        tile.Click += async (_, _) =>
        {
            if (_dragged)
            {
                _dragged = false; // fue un arrastre, no un toque
                return;
            }

            await TapApp(app);
        };
        StartDragFrom(tile, app);
        tile.ContextMenu = MenuFor(app);
        return tile;
    }

    private static readonly Brush DropBrush = Frozen(Color.FromRgb(0x1F, 0x3A, 0x5F));

    private enum MoveStep
    {
        PickApp,
        PickPanel,
    }

    /// <summary>Mover con toques: <c>null</c> si no se está moviendo; si no, en qué paso va.</summary>
    private MoveStep? _moving;

    /// <summary>La app elegida para mover.</summary>
    private DeckApp? _moveApp;

    /// <summary>Mueve la app elegida a un panel (como al soltarla arrastrada) y sale del modo mover.</summary>
    private async Task MoveChosenTo(DeckPanel panel)
    {
        if (_moveApp is not { Handle: { } h } app)
        {
            return;
        }

        _moving = null;
        _moveApp = null;
        await Run(false, app.PanelId is null ? new Adopt(h, app.Name, panel.Id) : new SendTo(h, panel.Id));
    }
    private const string DragFormat = "FocusWShell.DeckApp";
    private Point? _dragStart;
    private bool _dragged;

    /// <summary>
    /// Arrastrar una app (ADR 0013): basta con moverla unos píxeles con el ratón o el dedo; si no, es un
    /// toque normal.
    /// </summary>
    private void StartDragFrom(Button tile, DeckApp app)
    {
        if (app.Handle is null)
        {
            return; // una app cerrada no se arrastra: se abre en su panel
        }

        tile.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        tile.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
        tile.PreviewMouseMove += (_, e) =>
        {
            if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var delta = e.GetPosition(this) - start;
            if (Math.Abs(delta.X) < 12 && Math.Abs(delta.Y) < 12)
            {
                return;
            }

            _dragStart = null;
            _dragged = true;
            DragDrop.DoDragDrop(tile, new DataObject(DragFormat, app), DragDropEffects.Move);
        };
    }

    /// <summary>Un panel como zona donde soltar: se ilumina al pasar por encima.</summary>
    private Border DropZone(UIElement content, DeckPanel panel)
    {
        var zone = new Border
        {
            Child = content,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 2, 0, 10),
            AllowDrop = true,
        };
        bool Accepts(DragEventArgs e) => e.Data.GetData(DragFormat) is DeckApp app && app.PanelId != panel.Id;
        zone.DragEnter += (_, e) =>
        {
            e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
            if (Accepts(e))
            {
                zone.Background = DropBrush;
            }
        };
        zone.DragOver += (_, e) =>
        {
            e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        zone.DragLeave += (_, _) => zone.Background = Brushes.Transparent;
        zone.Drop += async (_, e) =>
        {
            zone.Background = Brushes.Transparent;
            _dragged = false;
            if (e.Data.GetData(DragFormat) is DeckApp app && app.PanelId != panel.Id && app.Handle is { } h)
            {
                await Run(false, app.PanelId is null ? new Adopt(h, app.Name, panel.Id) : new SendTo(h, panel.Id));
            }
        };
        return zone;
    }

    /// <summary>
    /// Cabecera de un panel con su control de navegación: ◀ ▶ recorren sus pestañas (en pestañas o
    /// pantalla completa) y el botón de la presentación pasa a la siguiente.
    /// </summary>
    private DockPanel PanelHeader(DeckPanel panel)
    {
        var header = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = true };
        var tabs = panel.Apps.Where(a => a.State == ListItemState.InPanel && a.Handle is not null).ToList();
        bool cycles = panel.Template is PanelTemplate.Stack or PanelTemplate.Fill && tabs.Count > 1;
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        if (cycles)
        {
            // En pestañas, la visible es la primera: ▶ pone la segunda; ◀, la última.
            controls.Children.Add(NavButton("◀", $"Pestaña anterior de {panel.Name}", () => Run(false, new MakeActiveInPanel(tabs[^1].Handle!.Value))));
            controls.Children.Add(NavButton("▶", $"Pestaña siguiente de {panel.Name}", () => Run(false, new MakeActiveInPanel(tabs[1].Handle!.Value))));
        }

        var order = panel.Display is null
            ? new[] { PanelTemplate.Tiles, PanelTemplate.Stack, PanelTemplate.List }
            : new[] { PanelTemplate.Tiles, PanelTemplate.Stack, PanelTemplate.List, PanelTemplate.Fill };
        var next = order[(Array.IndexOf(order, panel.Template) + 1) % order.Length];
        var template = NavButton(TemplateName(panel.Template), $"Presentación de {panel.Name}: pasar a {TemplateName(next)}", () => Run(false, new SetPresentation(panel.Id, next)));
        template.Width = double.NaN;
        template.Padding = new Thickness(10, 0, 10, 0);
        template.ToolTip = $"Pasar a {TemplateName(next)}";
        controls.Children.Add(template);
        DockPanel.SetDock(controls, Dock.Right);
        header.Children.Add(controls);
        var title = Label($"{panel.Name.ToUpperInvariant()} · {panel.Where}");
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        return header;
    }

    private static string TemplateName(PanelTemplate template) => template switch
    {
        PanelTemplate.Stack => "Pestañas",
        PanelTemplate.Fill => "Pantalla completa",
        PanelTemplate.List => "Lista",
        _ => "Tiles",
    };

    private static Button NavButton(string text, string name, Func<Task> click)
    {
        var b = new Button
        {
            Content = text,
            Width = 40,
            Height = 36,
            Margin = new Thickness(4, 0, 0, 0),
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = TextBrush,
            Background = CardBrush,
            Cursor = Cursors.Hand,
            FontSize = 11,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: {name}");
        b.Click += async (_, _) => await click();
        return b;
    }

    /// <summary>Tocar una app: en modo mover, elegirla; si no, llevarla al foco (o abrirla).</summary>
    private async Task TapApp(DeckApp app)
    {
        if (_closing)
        {
            if (app.Handle is { } h)
            {
                // Como «Mover»: hecho lo pedido, el modo se apaga. La tarjeta dice «Cerrando…» al momento
                // y desaparece cuando la app se cierra (el deck se pone al día solo).
                _closing = false;
                _closingNow = h;
                Render();
                await _actions.CloseApp(h);

                // Si sigue abierta (preguntó algo y se canceló), la tarjeta vuelve a ser normal.
                await Task.Delay(5000);
                if (_closingNow == h)
                {
                    _closingNow = null;
                    Render();
                }
            }

            return;
        }

        if (_moving is not null)
        {
            if (app.Handle is not null)
            {
                _moveApp = app;
                _moving = MoveStep.PickPanel;
                Render();
            }

            return;
        }

        await Choose(app);
    }

    private async Task Choose(DeckApp app)
    {
        if (app.State == ListItemState.Closed)
        {
            if (!Docked)
            {
                _actions.Hide();
            }

            await _actions.OpenApp(app.AppKey, app.PanelId!);
            return;
        }

        if (app.Handle is not { } h || app.State == ListItemState.InFocus)
        {
            return;
        }

        await Run(true, app.PanelId is null ? [new Adopt(h, app.Name, DefaultPanel()), new Promote(h)] : [new Promote(h)]);
    }

    private ContextMenu MenuFor(DeckApp app)
    {
        var menu = new ContextMenu();
        void Item(ItemsControl parent, string header, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            parent.Items.Add(item);
        }

        if (app.Handle is not { } h)
        {
            Item(menu, "Abrir en su panel", () => Choose(app));
            return menu;
        }

        bool free = app.PanelId is null;
        Item(menu, "Al foco", () => Choose(app), app.State != ListItemState.InFocus);
        var fullscreen = new MenuItem { Header = "Pantalla completa en" };
        foreach (var (display, name) in _state!.Displays)
        {
            Item(fullscreen, name, () => Run(true, free ? [new Adopt(h, app.Name, DefaultPanel()), new EnterFullscreen(h, display)] : [new EnterFullscreen(h, display)]));
        }

        menu.Items.Add(fullscreen);
        if (!free)
        {
            Item(menu, app.State == ListItemState.Parked ? "Recuperar" : "Estacionar",
                () => Run(false, app.State == ListItemState.Parked ? new Restore(h) : new Park(h)));
        }

        var move = new MenuItem { Header = free ? "Poner en" : "Mover a" };
        foreach (var panel in _state.Panels.Where(p => p.Id != app.PanelId))
        {
            Item(move, panel.Name, () => Run(false, free ? new Adopt(h, app.Name, panel.Id) : new SendTo(h, panel.Id)));
        }

        menu.Items.Add(move);
        if (!free)
        {
            menu.Items.Add(new Separator());
            Item(menu, "Liberar (devolverla a su sitio original)", () => Run(false, new Release(h)));
        }

        return menu;
    }

    private string DefaultPanel() =>
        _state!.Panels.FirstOrDefault(p => p.Where.StartsWith("principal, derecha", StringComparison.Ordinal))?.Id ?? _state.Panels[0].Id;

    /// <summary>
    /// "Mandar el deck a…": la esquina del principal (se oculta solo) o un monitor, en una mitad o entero;
    /// entero sólo si ese monitor no tiene paneles.
    /// </summary>
    private ContextMenu SendMenu(DeckState state)
    {
        var menu = new ContextMenu();
        var current = _actions.CurrentDock();
        void Item(string header, DeckDock? dock, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, IsCheckable = true, IsChecked = Equals(current, dock) };
            item.Click += (_, _) => _actions.Dock(dock);
            menu.Items.Add(item);
        }

        Item("Esquina del monitor principal (se oculta solo)", null);
        foreach (var (display, name) in state.Displays.Where(d => d.Display is not null))
        {
            bool hasPanels = state.Panels.Any(p => string.Equals(p.Display, display, StringComparison.OrdinalIgnoreCase));
            menu.Items.Add(new Separator());
            Item($"{name}: mitad izquierda", new DeckDock(display!, DeckZone.LeftHalf));
            Item($"{name}: mitad derecha", new DeckDock(display!, DeckZone.RightHalf));
            Item(hasPanels ? $"{name}: entero (tiene paneles)" : $"{name}: entero", new DeckDock(display!, DeckZone.Whole), !hasPanels);
        }

        return menu;
    }

    /// <summary>Fijado en un monitor, el deck no se oculta al elegir: vive ahí.</summary>
    private bool Docked => _actions.CurrentDock() is not null;

    /// <summary>Ejecuta comandos; si <paramref name="hide"/> (se eligió una app), el deck de la esquina se oculta.</summary>
    private async Task Run(bool hide, params Command[] commands)
    {
        if (hide && !Docked)
        {
            _actions.Hide();
        }

        await _actions.Run(commands);
    }

    private static Button WideButton(string text, bool enabled, Action click) => WideButton(text, enabled, () =>
    {
        click();
        return Task.CompletedTask;
    });

    private static Button WideButton(string text, bool enabled, Func<Task> click)
    {
        var b = new Button
        {
            Content = text,
            MinWidth = 150,
            Height = Touch,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(12, 0, 12, 0),
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = enabled ? TextBrush : DimBrush,
            Background = CardBrush,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: {text.TrimStart('⇄', '↓', '◧', '▣', '◨', '☀', '☾', '⟲', ' ')}");
        b.Click += async (_, _) =>
        {
            if (enabled)
            {
                await click();
            }
        };
        return b;
    }

    private static Button SmallButton(string text, string name, Action click)
    {
        var b = new Button
        {
            Content = text,
            Width = Touch,
            Height = Touch,
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = TextBrush,
            Background = CardBrush,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: {name}");
        b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Foreground = DimBrush, FontSize = 11, Margin = new Thickness(2, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
