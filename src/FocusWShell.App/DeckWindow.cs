using System.Windows;
using System.Windows.Controls;
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
    Func<DeckDock?> CurrentDock);

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
    private readonly StackPanel _root = new() { Margin = new Thickness(10) };
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
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly };
        SourceInitialized += (_, _) => OwnWindows.MakeToolNoActivate(new WindowInteropHelper(this).Handle);

        // Al ocultarse, se olvida si se estaba moviendo una app: al volver, el deck empieza limpio.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _moving = null;
                _moveApp = null;
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

    private void Render()
    {
        var state = _state!;
        _root.Children.Clear();

        // Cabecera: el foco y sus botones; cerrar.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
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
        focus.Children.Add(new TextBlock
        {
            Text = state.Focus is { } f ? f.Name : "vacío",
            Foreground = state.Focus is null ? DimBrush : TextBrush,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (state.Focus is { Title.Length: > 0 } ft)
        {
            focus.Children.Add(new TextBlock { Text = ft.Title, Foreground = DimBrush, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        header.Children.Add(focus);
        _root.Children.Add(header);

        var focusButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        focusButtons.Children.Add(WideButton("⇄  Anterior", state.HasPrevious, () => Run(true, new SwapWithPrevious())));
        focusButtons.Children.Add(WideButton("↓  Foco al panel", state.Focus is not null, () => Run(false, new ReturnToPanel(state.Focus!.Handle!.Value))));
        var moveButton = WideButton("⇆  Mover una app", true, () =>
        {
            _moving = _moving is null ? MoveStep.PickApp : null;
            _moveApp = null;
            Render();
        });
        if (_moving is not null)
        {
            moveButton.Background = ActiveBrush;
        }

        focusButtons.Children.Add(moveButton);
        _root.Children.Add(focusButtons);

        // Mover con toques (para la tablet, donde arrastrar desplaza la lista): la app y luego su panel.
        if (_moving is not null)
        {
            // Si la app elegida ya no está (se cerró), se vuelve a elegir.
            if (_moveApp is { Handle: { } chosen } && !state.Panels.SelectMany(p => p.Apps).Concat(state.Free).Any(a => a.Handle == chosen))
            {
                _moveApp = null;
                _moving = MoveStep.PickApp;
            }

            var banner = new DockPanel { Margin = new Thickness(0, 0, 0, 10), Background = DropBrush };
            var cancel = WideButton("Cancelar", true, () =>
            {
                _moving = null;
                _moveApp = null;
                Render();
            });
            cancel.Margin = new Thickness(6);
            DockPanel.SetDock(cancel, Dock.Right);
            banner.Children.Add(cancel);
            banner.Children.Add(new TextBlock
            {
                Text = _moveApp is null ? "Toca la app que quieres mover." : $"Mover {_moveApp.Name}: toca «Mover aquí» en su nuevo panel.",
                Foreground = TextBrush,
                FontSize = 14,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            _root.Children.Add(banner);
        }

        // Vistas.
        _root.Children.Add(Label("VISTA"));
        var views = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
        foreach (var (view, text) in new[] { (FocusPosition.Left, "◧  Izquierda"), (FocusPosition.Center, "▣  Centro"), (FocusPosition.Right, "◨  Derecha") })
        {
            var b = WideButton(text, true, () => Run(false, new SetView(view)));
            if (state.View == view)
            {
                b.Background = ActiveBrush;
            }

            views.Children.Add(b);
        }

        _root.Children.Add(views);

        // Ancho del foco: los presets de la vista; el actual, resaltado.
        var widths = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (name, weights) in ViewProportions.Presets(state.View))
        {
            var b = WideButton(name, true, () => Run(false, new SetProportions(state.View, weights)));
            if (weights.SameAs(state.Weights))
            {
                b.Background = ActiveBrush;
            }

            widths.Children.Add(b);
        }

        _root.Children.Add(widths);

        // Paneles con sus apps. Cada uno es una zona donde soltar una app arrastrada.
        foreach (var panel in state.Panels)
        {
            _root.Children.Add(PanelHeader(panel));
            var apps = new WrapPanel();
            if (panel.Apps.Count == 0)
            {
                apps.Children.Add(new TextBlock { Text = "sin apps (suelta aquí una app)", Foreground = DimBrush, Margin = new Thickness(4) });
            }

            foreach (var app in panel.Apps)
            {
                apps.Children.Add(AppTile(app));
            }

            _root.Children.Add(DropZone(apps, panel));
        }

        // Ventanas libres.
        if (state.Free.Count > 0)
        {
            _root.Children.Add(Label("LIBRES"));
            var free = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
            foreach (var app in state.Free)
            {
                free.Children.Add(AppTile(app));
            }

            _root.Children.Add(free);
        }

        // Escenas y botones fijos.
        if (state.Scenes.Count > 0)
        {
            _root.Children.Add(Label("ESCENAS"));
            var scenes = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
            foreach (var name in state.Scenes)
            {
                scenes.Children.Add(WideButton(name, true, () =>
                {
                    _actions.ApplyScene(name);
                    if (!Docked)
                    {
                        _actions.Hide();
                    }
                }));
            }

            _root.Children.Add(scenes);
        }

        var fixedRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var awake = WideButton(_actions.KeepAwake() ? "☀  Pantalla encendida: sí" : "☾  Pantalla encendida: no", true, () =>
        {
            _actions.ToggleKeepAwake();
            Render();
        });
        if (_actions.KeepAwake())
        {
            awake.Background = AwakeBrush;
        }

        fixedRow.Children.Add(awake);
        bool armed = DateTime.Now - _restoreArmed < TimeSpan.FromSeconds(4);
        var restore = WideButton(armed ? "⟲  ¿Seguro? Toca otra vez" : "⟲  Restaurar todas", true, () =>
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

        fixedRow.Children.Add(restore);
        _root.Children.Add(fixedRow);
    }

    /// <summary>
    /// Una app: tocarla la lleva al foco (si está cerrada, la abre en su panel; si es libre, la adopta y la
    /// lleva al foco). Clic derecho o pulsación larga: su menú.
    /// </summary>
    private Button AppTile(DeckApp app)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new Image
        {
            Source = app.App is { } catalog ? AppIcons.Get(catalog) : null,
            Width = 28,
            Height = 28,
            Opacity = app.State == ListItemState.Closed ? 0.45 : 1,
            Margin = new Thickness(0, 2, 0, 4),
        });
        content.Children.Add(new TextBlock
        {
            Text = app.Name + app.State switch
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
            MaxWidth = 92,
        });

        var tile = new Button
        {
            Content = content,
            Width = 104,
            Height = 76,
            Margin = new Thickness(0, 0, 6, 6),
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
        else if (_moving is not null && app.Handle is null)
        {
            tile.Opacity = 0.4; // una app cerrada no se mueve: se abre en su panel
        }

        tile.Click += async (_, _) =>
        {
            if (_dragged)
            {
                _dragged = false; // fue un arrastre, no un toque
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
        if (_moving == MoveStep.PickPanel && _moveApp is { } moving && moving.PanelId != panel.Id)
        {
            var here = NavButton("⬇  Mover aquí", $"Mover {moving.Name} a {panel.Name}", () => MoveChosenTo(panel));
            here.Width = double.NaN;
            here.Padding = new Thickness(14, 0, 14, 0);
            here.Background = ActiveBrush;
            here.FontSize = 13;
            controls.Children.Add(here);
        }

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
