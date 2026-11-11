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

/// <summary>
/// Lista tipo Explorador de un panel (ADR 0015). Ocupa el área del panel bajo su tira; las ventanas del
/// panel quedan detrás, sin minimizar, y en "Iconos grandes" se ven como miniaturas en vivo de DWM. Tocar
/// una app la lleva al foco; tocar una cerrada la abre en este panel. No roba el foco.
/// </summary>
public sealed class ListPanelWindow : Window
{
    private static readonly Brush Background0 = Frozen(Color.FromRgb(0x17, 0x1A, 0x21));
    private static readonly Brush CardBrush = Frozen(Color.FromRgb(0x24, 0x29, 0x33));
    private static readonly Brush CardHover = Frozen(Color.FromRgb(0x2F, 0x36, 0x43));
    private static readonly Brush ActiveBrush = Frozen(Color.FromRgb(0x28, 0x78, 0xDC));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE9, 0xEF));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x8A, 0x93, 0xA2));
    private static readonly Brush ThumbBrush = Frozen(Color.FromRgb(0x10, 0x12, 0x17));

    private const double CardWidth = 260;
    private const double ThumbHeight = 160;

    private readonly Func<Command, Task> _run;
    private readonly Func<string, string, Task> _openApp;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _modes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 6, 8, 2) };

    /// <summary>Nombre del panel: una lista sin ventanas abiertas no tiene tira de pestañas encima.</summary>
    private readonly TextBlock _title = new() { Foreground = DimBrush, FontSize = 11, Margin = new Thickness(12, 8, 8, 2), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Texto cuando no hay ninguna app que mostrar.</summary>
    private static TextBlock EmptyText() => new()
    {
        Text = Strings.List_Empty,
        Foreground = DimBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16),
    };

    /// <summary>Huecos de las miniaturas, por ventana.</summary>
    private readonly Dictionary<nint, Border> _slots = [];

    /// <summary>Miniaturas registradas, por ventana.</summary>
    private readonly Dictionary<nint, DwmThumbnail> _thumbs = [];

    private ListPanelView _view;
    private string _rendered = "";
    private nint _handle;

    public ListPanelWindow(ListPanelView view, Func<Command, Task> run, Func<string, string, Task> openApp)
    {
        _view = view;
        _run = run;
        _openApp = openApp;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Background = Background0;
        Title = $"FocusWShell · lista {view.Title}";
        var root = new DockPanel();
        var header = new DockPanel();
        DockPanel.SetDock(_modes, Dock.Right);
        header.Children.Add(_modes);
        header.Children.Add(_title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_scroll);
        Content = root;
        _scroll.ScrollChanged += (_, _) => UpdateThumbnails();
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(UpdateThumbnails, System.Windows.Threading.DispatcherPriority.Loaded);
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            OwnWindows.MakeToolNoActivate(_handle);
        };
        Closed += (_, _) => ClearThumbnails();
    }

    public string Key => _view.Key;

    /// <summary>Ventana del panel que está en primer plano encima de la lista, si la hay.</summary>
    public nint Peeking { get; set; }

    /// <summary>Si <paramref name="hwnd"/> es una de las ventanas que esta lista tiene detrás.</summary>
    public bool Holds(nint hwnd) => _view.Items.Any(i => i.Handle == hwnd && i.State == ListItemState.InPanel);

    /// <summary>Vuelve a ponerse encima de sus ventanas, sin activarse.</summary>
    public void Raise()
    {
        if (IsVisible)
        {
            OwnWindows.Place(new WindowInteropHelper(this).EnsureHandle(), _view.Area);
        }
    }

    public void Update(ListPanelView view)
    {
        _view = view;
        if (view.Signature != _rendered)
        {
            Render();
        }

        if (!IsVisible)
        {
            Show();
        }

        OwnWindows.Place(new WindowInteropHelper(this).EnsureHandle(), view.Area);
        Dispatcher.BeginInvoke(UpdateThumbnails, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Render()
    {
        _rendered = _view.Signature;
        _slots.Clear();
        // Con alguna ventana abierta, la tira de pestañas ya lleva el nombre del panel.
        _title.Text = _view.Items.Any(i => i.Handle is not null) ? "" : _view.Title.ToUpperInvariant();
        RenderModes();
        _scroll.Content = _view.Items.Count == 0 ? EmptyText() : _view.Layout switch
        {
            ListLayout.List => RenderList(),
            ListLayout.Details => RenderDetails(),
            _ => RenderLargeIcons(),
        };

        // Las miniaturas de ventanas que ya no están (o de otro modo de lista) se quitan.
        foreach (var gone in _thumbs.Keys.Where(h => !_slots.ContainsKey(h)).ToList())
        {
            _thumbs[gone].Dispose();
            _thumbs.Remove(gone);
        }
    }

    private void RenderModes()
    {
        _modes.Children.Clear();
        foreach (var (layout, label) in new[] { (ListLayout.LargeIcons, "▦ " + Strings.List_LargeIcons), (ListLayout.List, "≡ " + Strings.Tpl_List), (ListLayout.Details, "☰ " + Strings.List_Details) })
        {
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(4, 0, 0, 0),
                Focusable = false,
                BorderThickness = new Thickness(0),
                Foreground = TextBrush,
                Background = _view.Layout == layout ? ActiveBrush : CardBrush,
                Cursor = Cursors.Hand,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, Core.Text.F(Strings.List_ModeName, _view.Title, label[2..]));
            button.Click += async (_, _) => await _run(new SetListLayout(_view.PanelId, layout));
            _modes.Children.Add(button);
        }
    }

    private WrapPanel RenderLargeIcons()
    {
        var wrap = new WrapPanel { Margin = new Thickness(8) };
        foreach (var item in _view.Items)
        {
            var thumb = new Border { Height = ThumbHeight, Background = ThumbBrush, CornerRadius = new CornerRadius(4) };
            if (item.Handle is { } handle && item.State != ListItemState.Closed)
            {
                _slots[handle] = thumb;
            }
            else
            {
                // Cerrada: su icono, grande y apagado.
                thumb.Child = new Image { Source = item.App is { } app ? AppIcons.Get(app) : null, Width = 48, Height = 48, Opacity = 0.5 };
            }

            var caption = new DockPanel { Margin = new Thickness(8, 6, 8, 6) };
            caption.Children.Add(AppIcon(item, 16));
            caption.Children.Add(new TextBlock
            {
                Text = item.Name + StateSuffix(item.State),
                Foreground = item.State == ListItemState.Closed ? DimBrush : TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var stack = new StackPanel();
            stack.Children.Add(thumb);
            stack.Children.Add(caption);
            wrap.Children.Add(Card(item, stack, CardWidth, new Thickness(6)));
        }

        return wrap;
    }

    private WrapPanel RenderList()
    {
        var wrap = new WrapPanel { Margin = new Thickness(8), Orientation = Orientation.Horizontal };
        foreach (var item in _view.Items)
        {
            var row = new DockPanel { Margin = new Thickness(8, 5, 8, 5) };
            row.Children.Add(AppIcon(item, 16));
            row.Children.Add(new TextBlock
            {
                Text = item.Name + StateSuffix(item.State),
                Foreground = item.State == ListItemState.Closed ? DimBrush : TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            wrap.Children.Add(Card(item, row, 240, new Thickness(3)));
        }

        return wrap;
    }

    private StackPanel RenderDetails()
    {
        var list = new StackPanel { Margin = new Thickness(8) };
        var header = DetailRow(Strings.List_ColName, Strings.Main_ColTitle, Strings.Main_ColState, DimBrush);
        list.Children.Add(header);
        foreach (var item in _view.Items)
        {
            var row = DetailRow(item.Name, item.Title, StateText(item.State), item.State == ListItemState.Closed ? DimBrush : TextBrush);
            ((DockPanel)row.Child).Children.Insert(0, AppIcon(item, 16));
            list.Children.Add(Card(item, row, double.NaN, new Thickness(0, 1, 0, 1)));
        }

        return list;
    }

    private static Border DetailRow(string name, string title, string state, Brush brush)
    {
        var row = new DockPanel { Margin = new Thickness(8, 5, 8, 5), LastChildFill = true };
        var stateText = new TextBlock { Text = state, Width = 140, Foreground = DimBrush };
        DockPanel.SetDock(stateText, Dock.Right);
        row.Children.Add(stateText);
        row.Children.Add(new TextBlock { Text = name, Width = 200, Foreground = brush, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(new TextBlock { Text = title, Foreground = DimBrush, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border { Child = row };
    }

    /// <summary>Una tarjeta pulsable: abierta, al foco; cerrada, la abre en este panel.</summary>
    private Border Card(ListItem item, UIElement content, double width, Thickness margin)
    {
        var card = content as Border is { } existing && existing.Child is not null ? existing : new Border { Child = content };
        card.Width = width;
        card.Margin = margin;
        card.Background = CardBrush;
        card.CornerRadius = new CornerRadius(6);
        card.Cursor = Cursors.Hand;
        card.ToolTip = item.State switch
        {
            ListItemState.Closed => Core.Text.F(Strings.List_TipClosed, item.Name),
            ListItemState.InFocus => Core.Text.F(Strings.List_TipInFocus, item.Name),
            _ => Core.Text.F(Strings.List_TipOpen, item.Name, item.Title),
        };
        System.Windows.Automation.AutomationProperties.SetName(card, Core.Text.F(Strings.List_CardName, _view.Title, item.Name, StateText(item.State)));
        card.MouseEnter += (_, _) => card.Background = CardHover;
        card.MouseLeave += (_, _) => card.Background = CardBrush;
        card.MouseLeftButtonUp += async (_, _) =>
        {
            if (item.State == ListItemState.Closed)
            {
                await _openApp(item.AppKey, _view.PanelId);
            }
            else if (item.Handle is { } h && item.State != ListItemState.InFocus)
            {
                await _run(new Promote(h));
            }
        };
        return card;
    }

    private static Image AppIcon(ListItem item, double size) =>
        new() { Source = item.App is { } app ? AppIcons.Get(app) : null, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    private static string StateSuffix(ListItemState state) => state switch
    {
        ListItemState.InFocus => "  ↑",
        ListItemState.Parked => "  ⏸",
        ListItemState.Detached => "  ↗",
        _ => "",
    };

    private static string StateText(ListItemState state) => state switch
    {
        ListItemState.InPanel => Strings.ListState_InPanel,
        ListItemState.InFocus => Strings.ListState_InFocus,
        ListItemState.Parked => Strings.ListState_Parked,
        ListItemState.Detached => Strings.ListState_Detached,
        ListItemState.Closed => Strings.ListState_Closed,
        _ => "",
    };

    /// <summary>
    /// Coloca cada miniatura sobre su hueco, con la proporción de su ventana. Las que quedan fuera de la
    /// zona visible del scroll se ocultan: DWM las dibujaría encima de lo demás.
    /// </summary>
    private void UpdateThumbnails()
    {
        if (_handle == 0 || !IsVisible || _view.Layout != ListLayout.LargeIcons)
        {
            foreach (var t in _thumbs.Values)
            {
                t.Show(default, visible: false);
            }

            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var viewport = new Rect(_scroll.TranslatePoint(new Point(0, 0), this), new Size(_scroll.ViewportWidth, _scroll.ViewportHeight));
        foreach (var (handle, slot) in _slots)
        {
            if (!_thumbs.TryGetValue(handle, out var thumb))
            {
                if (DwmThumbnail.Register(_handle, handle) is not { } created)
                {
                    continue;
                }

                thumb = created;
                _thumbs[handle] = thumb;
            }

            if (!slot.IsVisible || slot.ActualWidth <= 0)
            {
                thumb.Show(default, visible: false);
                continue;
            }

            var box = new Rect(slot.TranslatePoint(new Point(0, 0), this), new Size(slot.ActualWidth, slot.ActualHeight));
            if (!viewport.Contains(box))
            {
                thumb.Show(default, visible: false);
                continue;
            }

            // Encajar la ventana en el hueco sin deformarla.
            var source = WindowMover.Observe(handle).Visible;
            double aspect = source.Width > 0 && source.Height > 0 ? (double)source.Width / source.Height : box.Width / box.Height;
            double w = box.Width, h = box.Width / aspect;
            if (h > box.Height)
            {
                h = box.Height;
                w = box.Height * aspect;
            }

            double x = box.X + ((box.Width - w) / 2), y = box.Y + ((box.Height - h) / 2);
            thumb.Show(new PixelRect(
                (int)Math.Round(x * dpi.DpiScaleX), (int)Math.Round(y * dpi.DpiScaleY),
                (int)Math.Round((x + w) * dpi.DpiScaleX), (int)Math.Round((y + h) * dpi.DpiScaleY)));
        }
    }

    private void ClearThumbnails()
    {
        foreach (var t in _thumbs.Values)
        {
            t.Dispose();
        }

        _thumbs.Clear();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
