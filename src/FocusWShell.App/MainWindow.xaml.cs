using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;
using FocusWShell.Win32.Persistence;
using FocusWShell.Win32.Scenes;

namespace FocusWShell.App;

/// <summary>Lista de ventanas, paneles y comandos. El estado real vive en el modelo del controlador.</summary>
public partial class MainWindow : Window
{
    private readonly ShellController _controller;
    private readonly ObservableCollection<WindowRow> _rows = [];

    /// <summary>Destinos que el usuario ha cambiado y aún no ha aplicado, por handle.</summary>
    private readonly Dictionary<nint, string> _pending = [];

    private bool _buildingPanels;

    /// <summary>Opciones de la columna Panel: libre, al foco y cada panel.</summary>
    public ObservableCollection<AssignmentOption> AssignmentOptions { get; } = [];

    private readonly AppSettings _settings;
    private bool _loadingGestures;

    public MainWindow(ShellController controller, AppSettings settings)
    {
        _controller = controller;
        _settings = settings;
        InitializeComponent();
        _loadingGestures = true;
        GestureAltTab.IsChecked = settings.FocusOnAltTab;
        GestureTaskView.IsChecked = settings.FocusOnTaskView;
        GestureTaskbar.IsChecked = settings.FocusOnTaskbar;
        GestureClick.IsChecked = settings.FocusOnClick;
        OpenMissingBox.IsChecked = settings.OpenMissingApps;
        _loadingGestures = false;
        DataContext = this;
        WindowList.ItemsSource = _rows;
        LoadScenes();
        _controller.ScenesChanged += () => Dispatcher.BeginInvoke(LoadScenes);
        _controller.CatalogChanged += () => Dispatcher.BeginInvoke(() => BuildCatalog(_controller.Panels()));
        Refresh();
    }

    private bool _showingView;

    /// <summary>Orden de las opciones de <c>ViewCombo</c>.</summary>
    private static readonly FocusPosition[] Views = [FocusPosition.Left, FocusPosition.Center, FocusPosition.Right];

    private void ShowView(FocusPosition view)
    {
        _showingView = true;
        ViewCombo.SelectedIndex = Array.IndexOf(Views, view);

        // Presets de la vista y, si las proporciones son otras (separador arrastrado), "Personalizadas".
        var weights = _controller.Proportions;
        var options = ViewProportions.Presets(view).Select(p => (Label: $"{p.Name} · {p.Weights.Describe(view)}", p.Weights)).ToList();
        int selected = options.FindIndex(o => o.Weights.SameAs(weights));
        if (selected < 0)
        {
            options.Add(($"Personalizadas · {weights.Describe(view)}", weights));
            selected = options.Count - 1;
        }

        ProportionsCombo.ItemsSource = options.Select(o => o.Label).ToList();
        ProportionsCombo.Tag = options.Select(o => o.Weights).ToList();
        ProportionsCombo.SelectedIndex = selected;
        _showingView = false;
    }

    private async void ProportionsCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_showingView || ProportionsCombo.SelectedIndex < 0 || ProportionsCombo.Tag is not List<ViewWeights> options)
        {
            return;
        }

        var weights = options[ProportionsCombo.SelectedIndex];
        var view = _controller.View;
        var result = await Task.Run(() => _controller.Execute(new SetProportions(view, weights)));
        Refresh();
        SetStatus(Describe(result));
    }

    /// <summary>Cambiar de vista mueve el foco de sitio sin cambiar qué ventana hay en él (ADR 0012).</summary>
    private async void ViewCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_showingView || ViewCombo.SelectedIndex < 0 || _controller is null)
        {
            return;
        }

        var view = Views[ViewCombo.SelectedIndex];
        var result = await Task.Run(() => _controller.Execute(new SetView(view)));
        Refresh();
        SetStatus(Describe(result));
    }

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

        Activate();
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
        string missing = p is not null && _controller.IsDisplayMissing(p.Display) ? " (monitor desconectado)" : "";
        return m switch
        {
            { Placement: Placement.Parked } => $"Estacionada en {name}{missing}",
            { Detached: true } => "Fuera de layout",
            { Placement: Placement.Center } => $"Foco (vuelve a {name})",
            { Placement: Placement.Fullscreen } => $"Pantalla completa (vuelve a {name})",
            _ => $"En {name}{missing}",
        };
    }

    /// <summary>Nombre de un monitor para la UI.</summary>
    private static string DisplayName(string? key, IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux) =>
        key is null ? "Principal" : aux.FirstOrDefault(d => d.Key == key) is { } d ? d.FriendlyName ?? d.DeviceName.TrimStart('\\', '.') : "monitor desconectado";

    private static string LocationLabel(Panel p, IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux) =>
        p.Display is null
            ? $"principal, {(p.Side == PanelSide.Left ? "izquierda" : "derecha")} del foco"
            : $"{DisplayName(p.Display, aux)}, {(p.Side == PanelSide.Left ? "izquierda" : "derecha")}";

    public void Refresh()
    {
        var display = ShellController.TargetDisplay();
        var aux = ShellController.AuxiliaryDisplays();
        HeaderText.Text =
            $"Principal {display.FriendlyName ?? display.DeviceName} · {display.WorkArea} · {display.Dpi} DPI" +
            string.Concat(aux.Select(a => $"   ·   Auxiliar {a.FriendlyName ?? a.DeviceName} · {a.WorkArea} · {a.Dpi} DPI")) + "\n" +
            "Atajos Ctrl+Alt+Mayús: ↑ al foco · ← anterior · ↓ foco al panel · F pantalla completa · V vista · P pantalla encendida · Retroceso restaurar todas";
        var fullscreenTargets = new List<string> { "Principal" };
        fullscreenTargets.AddRange(aux.Select(a => a.FriendlyName ?? a.DeviceName));
        int fullscreenIndex = Math.Max(0, FullscreenTarget.SelectedIndex);
        FullscreenTarget.ItemsSource = fullscreenTargets;
        FullscreenTarget.SelectedIndex = Math.Min(fullscreenIndex, fullscreenTargets.Count - 1);
        ShowView(_controller.View);

        // Primero se desconectan las filas: al vaciar las opciones, WPF les escribe un valor vacío.
        foreach (var row in _rows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        var panels = _controller.Panels();
        AssignmentOptions.Clear();
        AssignmentOptions.Add(new AssignmentOption(AssignmentOption.Free, "— libre"));
        AssignmentOptions.Add(new AssignmentOption(AssignmentOption.Focus, "Al foco"));
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
        foreach (var w in windows)
        {
            managed.TryGetValue(w.Handle, out var m);
            var assignment = _pending.TryGetValue(w.Handle, out var p) ? p : m is null ? AssignmentOption.Free : AssignmentOf(m);
            var row = new WindowRow(w, assignment) { State = StateOf(m, byId) };
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        foreach (var gone in _pending.Keys.Except(windows.Select(w => w.Handle)).ToList())
        {
            _pending.Remove(gone);
        }

        WindowList.SelectedItem = _rows.FirstOrDefault(r => r.Snapshot.Handle == selected);
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
            $"{where} · {(slot == PanelSlot.Upper ? "arriba" : "abajo")}{Occupant(display, side, slot)}";
        var locations = new List<Location>();
        void AddColumn(string? display, PanelSide side, string where)
        {
            locations.Add(new(display, side, PanelSlot.Upper, Spot(where, display, side, PanelSlot.Upper)));
            locations.Add(new(display, side, PanelSlot.Lower, Spot(where, display, side, PanelSlot.Lower)));
        }

        AddColumn(null, PanelSide.Left, "Principal · izquierda del foco");
        AddColumn(null, PanelSide.Right, "Principal · derecha del foco");
        foreach (var a in aux)
        {
            string name = DisplayName(a.Key, aux);
            AddColumn(a.Key, PanelSide.Left, $"{name} · izquierda");
            AddColumn(a.Key, PanelSide.Right, $"{name} · derecha");
        }

        var presentations = new List<(PanelTemplate Template, string Label)>
        {
            (PanelTemplate.Tiles, "Tiles"),
            (PanelTemplate.Stack, "Pestañas"),
            (PanelTemplate.Fill, "Pantalla completa"),
        };

        System.Windows.Controls.ComboBox LocationCombo(string label, string? display, PanelSide side, PanelSlot slot)
        {
            var options = locations.ToList();
            if (display is not null && !options.Any(o => o.Display == display))
            {
                // Un monitor que ahora no está conectado: se muestra su sitio para no perderlo.
                options.Add(new(display, side, slot, Spot($"{DisplayName(display, aux)} · {(side == PanelSide.Left ? "izquierda" : "derecha")}", display, side, slot)));
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
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var name = new System.Windows.Controls.TextBox { Text = panel.Name, Width = 160, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(name, $"Nombre del panel {panel.Name}");
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

            var location = LocationCombo($"Ubicación del panel {panel.Name}", panel.Display, panel.Side, panel.Slot);
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
            System.Windows.Automation.AutomationProperties.SetName(presentation, $"Presentación del panel {panel.Name}");
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

            var delete = new System.Windows.Controls.Button { Content = "Borrar", Padding = new Thickness(8, 1, 8, 1) };
            System.Windows.Automation.AutomationProperties.SetName(delete, $"Borrar el panel {panel.Name}");
            delete.Click += async (_, _) =>
            {
                if (MessageBox.Show($"¿Borrar el panel «{panel.Name}»? Sus ventanas quedan libres, donde estén.", "FocusWShell",
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
            PanelsList.Children.Add(row);
        }

        // Fila para crear un panel nuevo.
        var create = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var newName = new System.Windows.Controls.TextBox { Width = 160, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(newName, "Nombre del panel nuevo");
        // Para el panel nuevo se propone el primer puesto libre: de un auxiliar si hay, si no del principal.
        var free = locations.Where(o => Occupant(o.Display, o.Side, o.Slot).Length == 0).ToList();
        var suggested = free.FirstOrDefault(o => o.Display is not null) ?? free.FirstOrDefault() ?? locations[0];
        var newLocation = LocationCombo("Ubicación del panel nuevo", suggested.Display, suggested.Side, suggested.Slot);
        var add = new System.Windows.Controls.Button { Content = "Crear panel", Padding = new Thickness(8, 1, 8, 1) };
        add.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(newName.Text) || newLocation.Tag is not List<Location> options || newLocation.SelectedIndex < 0)
            {
                SetStatus("Escribe un nombre y elige dónde va el panel nuevo.");
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
        PanelsList.Children.Add(create);
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
            CatalogList.Children.Add(new System.Windows.Controls.TextBlock { Text = "Todavía no hay apps: aparecerán al detectarlas abiertas.", Foreground = System.Windows.Media.Brushes.DimGray });
            return;
        }

        foreach (var (app, open) in apps)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            row.Children.Add(new System.Windows.Controls.Image
            {
                Source = AppIcons.Get(app),
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            // El nombre se edita en el sitio; se guarda al salir del cuadro.
            var name = new System.Windows.Controls.TextBox
            {
                Text = app.Name,
                Width = 200,
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = open ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = app.Hidden ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Black,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = app.ExecutablePath ?? app.Aumid ?? app.AppKey,
            };
            System.Windows.Automation.AutomationProperties.SetName(name, $"Nombre de la app {app.Name}");
            name.LostFocus += (_, _) =>
            {
                if (name.Text.Trim().Length > 0 && name.Text.Trim() != app.Name)
                {
                    _controller.RenameApp(app.AppKey, name.Text);
                }
            };
            row.Children.Add(name);
            row.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = open ? "abierta" : "cerrada",
                Width = 70,
                Foreground = System.Windows.Media.Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var target = new System.Windows.Controls.ComboBox
            {
                Width = 230,
                Margin = new Thickness(0, 0, 8, 0),
                ItemsSource = panels.Select(p => p.Name).ToList(),
                SelectedIndex = Math.Max(0, panels.ToList().FindIndex(p => p.Id == (app.LastPanelId ?? DefaultPanel()))),
            };
            System.Windows.Automation.AutomationProperties.SetName(target, $"Panel para abrir {app.Name}");

            var launch = new System.Windows.Controls.Button
            {
                Content = "Abrir",
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = app.CanLaunch,
                ToolTip = app.CanLaunch ? "Abre la app y pone su ventana en el panel elegido" : "No se sabe cómo abrirla",
            };
            System.Windows.Automation.AutomationProperties.SetName(launch, $"Abrir {app.Name}");
            launch.Click += async (_, _) =>
            {
                if (target.SelectedIndex < 0)
                {
                    return;
                }

                var panelId = panels[target.SelectedIndex].Id;
                var key = app.AppKey;
                launch.IsEnabled = false;
                SetStatus($"Abriendo {app.Name}…");
                SetStatus(await Task.Run(() => _controller.OpenApp(key, panelId)));
                Refresh();
            };

            var hide = new System.Windows.Controls.Button
            {
                Content = app.Hidden ? "Mostrar" : "Ocultar",
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = app.Hidden ? "Vuelve a mostrarla en la lista" : "La quita de la lista sin olvidarla: no vuelve a aparecer como nueva",
            };
            System.Windows.Automation.AutomationProperties.SetName(hide, $"{(app.Hidden ? "Mostrar" : "Ocultar")} {app.Name}");
            hide.Click += (_, _) => _controller.SetAppHidden(app.AppKey, !app.Hidden);

            var forget = new System.Windows.Controls.Button { Content = "Olvidar", Padding = new Thickness(8, 1, 8, 1) };
            System.Windows.Automation.AutomationProperties.SetName(forget, $"Olvidar {app.Name}");
            forget.Click += (_, _) => _controller.ForgetApp(app.AppKey);

            row.Children.Add(target);
            row.Children.Add(launch);
            row.Children.Add(hide);
            row.Children.Add(forget);
            CatalogList.Children.Add(row);
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WindowRow row || e.PropertyName != nameof(WindowRow.Assignment) || row.Assignment is null)
        {
            return;
        }

        _pending[row.Snapshot.Handle] = row.Assignment;

        // El foco aloja una sola ventana: la que lo tenía vuelve a su panel.
        if (row.Assignment == AssignmentOption.Focus)
        {
            var managed = _controller.Managed().ToDictionary(m => m.Handle);
            foreach (var other in _rows.Where(r => r != row && r.Assignment == AssignmentOption.Focus))
            {
                other.Assignment = managed.TryGetValue(other.Snapshot.Handle, out var m) ? m.PanelId : AssignmentOption.Free;
            }
        }
    }

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
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var commands = PendingCommands();
        if (commands.Count == 0)
        {
            SetStatus("No hay cambios de rol que aplicar.");
            return;
        }

        await Run(() => _controller.Execute(commands));
        _pending.Clear();
        Refresh();
    }

    private async void Promote_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowRow row)
        {
            SetStatus("Selecciona una ventana de la lista.");
            return;
        }

        var h = row.Snapshot.Handle;
        bool managed = _controller.Managed().Any(m => m.Handle == h);
        Command[] commands = managed ? [new Promote(h)] : [new Adopt(h, row.App, DefaultPanel()), new Promote(h)];
        _pending.Remove(h);
        await Run(() => _controller.Execute(commands));
        Refresh();
    }

    /// <summary>
    /// Pantalla completa (ADR 0012) de la ventana seleccionada en el monitor elegido; si ya lo está ahí,
    /// sale y vuelve al foco.
    /// </summary>
    private async void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowRow row)
        {
            SetStatus("Selecciona una ventana de la lista.");
            return;
        }

        var h = row.Snapshot.Handle;
        var aux = ShellController.AuxiliaryDisplays();
        string? display = FullscreenTarget.SelectedIndex > 0 && FullscreenTarget.SelectedIndex - 1 < aux.Count
            ? aux[FullscreenTarget.SelectedIndex - 1].Key
            : null;
        var current = _controller.Managed().FirstOrDefault(m => m.Handle == h);
        Command[] commands = current switch
        {
            null => [new Adopt(h, row.App, DefaultPanel()), new EnterFullscreen(h, display)],
            { Placement: Placement.Fullscreen } when _controller.FullscreenDisplay == display => [new ExitFullscreen(h)],
            _ => [new EnterFullscreen(h, display)],
        };
        _pending.Remove(h);
        await Run(() => _controller.Execute(commands));
        Refresh();
    }

    private async void Swap_Click(object sender, RoutedEventArgs e)
    {
        await Run(() => _controller.Execute(new SwapWithPrevious()));
        Refresh();
    }

    private async void Return_Click(object sender, RoutedEventArgs e)
    {
        await Run(_controller.ReturnCenter);
        Refresh();
    }

    private async void Park_Click(object sender, RoutedEventArgs e) => await OnSelected(h => new Park(h));

    private async void Unpark_Click(object sender, RoutedEventArgs e) => await OnSelected(h => new Restore(h));

    private async void Reattach_Click(object sender, RoutedEventArgs e) => await OnSelected(h => new Reattach(h));

    /// <summary>Ejecuta un comando sobre la ventana seleccionada.</summary>
    private async Task OnSelected(Func<nint, Command> command)
    {
        if (WindowList.SelectedItem is not WindowRow row)
        {
            SetStatus("Selecciona una ventana de la lista.");
            return;
        }

        _pending.Remove(row.Snapshot.Handle);
        await Run(() => _controller.Execute(command(row.Snapshot.Handle)));
        Refresh();
    }

    private async void Release_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowRow row)
        {
            SetStatus("Selecciona una ventana de la lista.");
            return;
        }

        _pending.Remove(row.Snapshot.Handle);
        await Run(() => _controller.Execute(new Release(row.Snapshot.Handle)));
        Refresh();
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        _pending.Clear();
        await Run(() => _controller.Execute(new RestoreAll()));
        Refresh();
    }

    private async Task Run(Func<ControllerResult> action)
    {
        var buttons = new[] { ApplyButton, RestoreButton, RefreshButton, PromoteButton, FullscreenButton, SwapButton, ReturnButton, ParkButton, UnparkButton, ReattachButton, ReleaseButton };
        foreach (var b in buttons)
        {
            b.IsEnabled = false;
        }

        try
        {
            var result = await Task.Run(action);
            SetStatus(Describe(result));
        }
        finally
        {
            foreach (var b in buttons)
            {
                b.IsEnabled = true;
            }
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

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
            SetStatus("No hay escenas guardadas. Coloca las ventanas, escribe un nombre y pulsa \"Guardar escena actual\".");
            return;
        }

        _pending.Clear();
        var result = await Task.Run(() => _controller.ApplySavedScene(name));
        SetStatus(result.Summary + Environment.NewLine + Describe(result.Result));
        Refresh();
    }

    private void SaveScene_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(SceneName.Text) ? SelectedScene() : SceneName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus("Escribe un nombre para la escena.");
            return;
        }

        var scene = _controller.SaveScene(name);
        SceneName.Text = "";
        SetStatus($"Escena «{scene.Name}» guardada: {scene.Windows.Count} ventanas, {scene.Templates.Count} templates.");
        LoadScenes();
        SceneCombo.SelectedIndex = Math.Max(0, (SceneCombo.Tag as IReadOnlyList<string>)?.ToList().FindIndex(n => n == scene.Name) ?? 0);
    }

    private void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedScene() is not { } name)
        {
            return;
        }

        if (MessageBox.Show($"¿Borrar la escena «{name}»? Las ventanas no se mueven.", "FocusWShell", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes)
        {
            _controller.DeleteScene(name);
            SetStatus($"Escena «{name}» borrada.");
        }
    }

    public static string Describe(ControllerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var sb = new StringBuilder();
        foreach (var r in result.Rejections)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"No se hizo: {r}");
        }

        foreach (var restore in result.Restores.Where(r => r.Entries.Count > 0))
        {
            sb.Append(Describe(restore));
        }

        if (result.Applied is { } report)
        {
            sb.Append(Describe(report));
        }

        return sb.Length == 0 ? "Hecho." : sb.ToString();
    }

    private static string Describe(SceneApplyReport report)
    {
        var sb = new StringBuilder();
        if (report.Error is not null)
        {
            sb.AppendLine(report.Error);
            return sb.ToString();
        }

        // Columnas del principal en el orden en que se ven.
        var layout = report.Layout.Principal;
        var columns = new[] { ("izquierda", layout.LeftColumn), ("foco", layout.CenterColumn), ("derecha", layout.RightColumn) }
            .OrderBy(c => c.Item2.Left)
            .Select(c => $"{c.Item1} {c.Item2.Width} px");
        sb.AppendLine(CultureInfo.CurrentCulture, $"Principal: {string.Join(" · ", columns)}");
        var names = FocusWShell.Win32.DisplayEnumerator.Enumerate().ToDictionary(d => d.Key, d => d.FriendlyName ?? d.DeviceName);
        foreach (var aux in report.Layout.Aux)
        {
            sb.AppendLine(
                CultureInfo.CurrentCulture,
                $"{names.GetValueOrDefault(aux.Display, aux.Display)}: {string.Join(" · ", aux.Columns.Select(c => $"{aux.Titles.GetValueOrDefault(c.Key, c.Key)} {c.Value.Width} px ({aux.EffectiveTemplates[c.Key]})"))}");
        }

        foreach (var note in report.Layout.Notes)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"  · {note}");
        }

        foreach (var pass in report.Passes.Where(p => p.Learned.Count > 0))
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"  · Aprendido: {string.Join("; ", pass.Learned)}. Se recolocó.");
        }

        foreach (var (target, result) in report.Applied.Where(a => a.Result is { IsExact: false }))
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"  ! {target.Label}: {result!.Error ?? $"desviación de {result.MaxDeviation} px"}");
        }

        var invaders = report.CenterInvaders;
        string center = invaders.Count == 0 ? "Centro protegido." : $"Invaden el Centro: {string.Join(", ", invaders)}.";
        sb.AppendLine(CultureInfo.CurrentCulture, $"{report.ExactCount}/{report.Applied.Count} ventanas en su sitio. {center}");
        return sb.ToString();
    }

    public static string Describe(RestoreReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.CurrentCulture, $"Restauradas: {report.Summary}");
        foreach (var e in report.Entries.Where(e => e.Outcome is RestoreOutcome.Pending or RestoreOutcome.RestoredWithDifferences))
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"  ! {e.Entry.ProcessName}: {e.Detail}");
        }

        return sb.ToString();
    }
}
