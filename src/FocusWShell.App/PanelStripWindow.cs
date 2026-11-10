using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;
using FocusWShell.Win32;

namespace FocusWShell.App;

/// <summary>
/// Tira de pestañas de un panel (03-arquitectura, "Overlays del gestor"). Vive en la franja reservada
/// encima de las ventanas del panel, así que nunca tapa a ninguna. No roba el foco al hacer clic.
/// </summary>
public sealed class PanelStripWindow : Window
{
    private static readonly Brush Background0 = Frozen(Color.FromRgb(0x1E, 0x22, 0x2B));
    private static readonly Brush ActiveBrush = Frozen(Color.FromRgb(0x28, 0x78, 0xDC));
    private static readonly Brush TabBrush = Frozen(Color.FromRgb(0x33, 0x39, 0x46));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE9, 0xEF));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x9A, 0xA3, 0xB2));

    private readonly Func<Command, Task> _run;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new()
    {
        Foreground = DimBrush,
        FontSize = 11,
        Margin = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private PanelStrip _strip;

    /// <summary>▲ filas a–b de n ▼, si el panel en tiles no cabe entero (ADR 0015).</summary>
    private readonly StackPanel _pager = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Primer clic pendiente de saber si es simple o doble.</summary>
    private (nint Handle, System.Windows.Threading.DispatcherTimer Timer)? _pendingClick;

    public PanelStripWindow(PanelStrip strip, Func<Command, Task> run)
    {
        _strip = strip;
        _run = run;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Background = Background0;
        Title = $"FocusWShell · {strip.Title}";
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_title, Dock.Left);
        root.Children.Add(_title);
        DockPanel.SetDock(_pager, Dock.Right);
        root.Children.Add(_pager);
        root.Children.Add(new ScrollViewer
        {
            Content = _tabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = root;

        // La rueda del ratón sobre la tira recorre las filas.
        MouseWheel += async (_, e) =>
        {
            if (_strip.Pages is { } pages)
            {
                await _run(new ScrollPanel(_strip.PanelId, pages.First + (e.Delta > 0 ? -1 : 1)));
            }
        };
        SourceInitialized += (_, _) => OwnWindows.MakeToolNoActivate(new WindowInteropHelper(this).Handle);
        Render();
    }

    public string Key => _strip.Key;

    /// <summary>Actualiza contenido y posición.</summary>
    public void Update(PanelStrip strip)
    {
        _strip = strip;
        Render();
        if (!IsVisible)
        {
            // Show() para que WPF la mida y la pinte (ShowActivated = false: no roba el foco).
            Show();
        }

        PlaceOnBand();
    }

    public void PlaceOnBand()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        OwnWindows.Place(handle, _strip.Band);
    }

    private void Render()
    {
        _title.Text = _strip.Title.ToUpperInvariant();
        _pager.Children.Clear();
        if (_strip.Pages is { } pages)
        {
            _pager.Children.Add(PagerButton("▲", "Filas de arriba", pages.First > 0, pages.First - 1));
            _pager.Children.Add(new TextBlock
            {
                Text = $"{pages.First + 1}–{pages.First + pages.Visible} / {pages.Total}",
                Foreground = DimBrush,
                FontSize = 11,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _pager.Children.Add(PagerButton("▼", "Filas de abajo", pages.First + pages.Visible < pages.Total, pages.First + 1));
        }

        _tabs.Children.Clear();
        foreach (var tab in _strip.Tabs)
        {
            _tabs.Children.Add(MakeTab(tab));
        }
    }

    private Button PagerButton(string text, string name, bool enabled, int row)
    {
        var button = new Button
        {
            Content = text,
            Width = 26,
            Margin = new Thickness(0, 3, 4, 3),
            Focusable = false,
            BorderThickness = new Thickness(0),
            // Sin más filas en ese sentido, atenuado (el estilo de "desactivado" de WPF pinta un fondo blanco).
            Foreground = enabled ? TextBrush : DimBrush,
            Background = enabled ? TabBrush : Background0,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{_strip.Title}: {name}");
        button.Click += async (_, _) =>
        {
            if (enabled)
            {
                await _run(new ScrollPanel(_strip.PanelId, row));
            }
        };
        return button;
    }

    private Button MakeTab(StripTab tab)
    {
        string prefix = tab.State switch
        {
            TabState.Parked => "⏸ ",
            TabState.InCenter => "↑ ",
            TabState.Detached => "↗ ",
            _ => "",
        };
        var button = new Button
        {
            Content = prefix + tab.Label,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 3, 4, 3),
            MinWidth = 60,
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = tab.State is TabState.Active or TabState.Hidden ? TextBrush : DimBrush,
            Background = tab.State == TabState.Active ? ActiveBrush : TabBrush,
            FontStyle = tab.State == TabState.Parked ? FontStyles.Italic : FontStyles.Normal,
            Cursor = Cursors.Hand,
            ToolTip = tab.State switch
            {
                TabState.Active => $"{tab.Label} · visible. Doble clic: al Centro.",
                TabState.Hidden => $"{tab.Label} · oculta (otra pestaña o fila). Clic: mostrarla. Doble clic: al Centro.",
                TabState.Parked => $"{tab.Label} · estacionada. Clic: recuperarla. Doble clic: al Centro.",
                TabState.InCenter => $"{tab.Label} · está en el Centro y volverá aquí. Clic: devolverla.",
                TabState.Detached => $"{tab.Label} · fuera de layout. Clic: reencajarla.",
                _ => tab.Label,
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{_strip.Title}: {tab.Label} ({tab.State})");

        // Clic: la acción natural según su estado. Doble clic: al Centro. El clic simple espera el
        // tiempo de doble clic del sistema: si actuara al momento, la tira se redibujaría entre los dos
        // clics y el segundo caería en otra pestaña.
        button.Click += async (_, _) =>
        {
            if (_pendingClick is { } pending && pending.Handle == tab.Handle)
            {
                pending.Timer.Stop();
                _pendingClick = null;
                if (tab.State != TabState.InCenter)
                {
                    await _run(new Promote(tab.Handle));
                }

                return;
            }

            _pendingClick?.Timer.Stop();
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime),
            };
            _pendingClick = (tab.Handle, timer);
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                _pendingClick = null;
                Command? command = tab.State switch
                {
                    TabState.Hidden => new MakeActiveInPanel(tab.Handle),
                    TabState.Parked => new Restore(tab.Handle),
                    TabState.InCenter => new ReturnToPanel(tab.Handle),
                    TabState.Detached => new Reattach(tab.Handle),
                    _ => null,
                };
                if (command is not null)
                {
                    await _run(command);
                }
            };
            timer.Start();
        };

        var menu = new ContextMenu();
        void Item(string header, Command command, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += async (_, _) => await _run(command);
            menu.Items.Add(item);
        }

        Item("Al Centro", new Promote(tab.Handle), tab.State != TabState.InCenter);
        Item("Estacionar", new Park(tab.Handle), tab.State != TabState.Parked);
        Item("Recuperar", new Restore(tab.Handle), tab.State == TabState.Parked);
        Item("Reencajar", new Reattach(tab.Handle), tab.State == TabState.Detached);
        if (_strip.Template == PanelTemplate.Tiles)
        {
            menu.Items.Add(new Separator());
            foreach (var (size, label) in new[] { (TileSize.Small, "Pequeño (1 columna)"), (TileSize.Medium, "Mediano (2 columnas)"), (TileSize.Large, "Grande (3 columnas)") })
            {
                var item = new MenuItem { Header = $"Tamaño: {label}", IsCheckable = true, IsChecked = tab.Size == size };
                item.Click += async (_, _) => await _run(new SetTileSize(tab.Handle, size));
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        Item("Liberar (devolverla a su sitio original)", new Release(tab.Handle));
        button.ContextMenu = menu;
        return button;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
