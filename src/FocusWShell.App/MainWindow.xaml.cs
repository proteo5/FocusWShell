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

/// <summary>Lista de ventanas, roles y comandos de M0. El estado real vive en el modelo del controlador.</summary>
public partial class MainWindow : Window
{
    private readonly ShellController _controller;
    private readonly ObservableCollection<WindowRow> _rows = [];

    /// <summary>Roles que el usuario ha cambiado y aún no ha aplicado, por handle.</summary>
    private readonly Dictionary<nint, SceneRole> _pending = [];

    /// <summary>Monitores que el usuario ha cambiado y aún no ha aplicado, por handle.</summary>
    private readonly Dictionary<nint, string> _pendingDisplay = [];

    private bool _buildingTemplates;

    /// <summary>Opciones de la columna Monitor: el principal y cada auxiliar conectado.</summary>
    public ObservableCollection<DisplayOption> DisplayOptions { get; } = [];

    public MainWindow(ShellController controller)
    {
        _controller = controller;
        InitializeComponent();
        DataContext = this;
        WindowList.ItemsSource = _rows;
        LoadScenes();
        _controller.ScenesChanged += () => Dispatcher.BeginInvoke(LoadScenes);
        Refresh();
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

    /// <summary>Rol que el modelo da a una ventana gestionada.</summary>
    private static SceneRole RoleOf(ManagedWindow m) => m.Placement == Placement.Center
        ? SceneRole.Center
        : m.Home == Home.Context ? SceneRole.Context : SceneRole.Work;

    private static string StateOf(ManagedWindow? m) => m switch
    {
        null => "",
        { Placement: Placement.Parked } => m.Display is null ? "Estacionada" : "Estacionada (auxiliar)",
        { Detached: true } => "Fuera de layout",
        { Placement: Placement.Center } => $"Centro (vuelve a {(m.Home == Home.Context ? "Contexto" : "Trabajo")}{(m.Display is null ? "" : " auxiliar")})",
        { Home: Home.Context } => m.Display is null ? "En Contexto" : "En Contexto auxiliar",
        _ => m.Display is null ? "En Trabajo" : "En Trabajo auxiliar",
    };

    public void Refresh()
    {
        var display = ShellController.TargetDisplay();
        var aux = ShellController.AuxiliaryDisplays();
        HeaderText.Text =
            $"Principal {display.FriendlyName ?? display.DeviceName} · {display.WorkArea} · {display.Dpi} DPI" +
            string.Concat(aux.Select(a => $"   ·   Auxiliar {a.FriendlyName ?? a.DeviceName} · {a.WorkArea} · {a.Dpi} DPI")) + "\n" +
            "Atajos Ctrl+Alt+Mayús: ↑ al Centro · ← anterior · ↓ Centro al panel · P pantalla encendida · Retroceso restaurar todas";

        DisplayOptions.Clear();
        DisplayOptions.Add(new DisplayOption(DisplayOption.PrincipalKey, $"Principal ({display.WorkArea.Width}×{display.WorkArea.Height})"));
        foreach (var a in aux)
        {
            DisplayOptions.Add(new DisplayOption(a.Key, $"{a.FriendlyName ?? a.DeviceName.TrimStart('\\', '.')} ({a.WorkArea.Width}×{a.WorkArea.Height})"));
        }

        BuildTemplates(aux);

        var selected = (WindowList.SelectedItem as WindowRow)?.Snapshot.Handle;
        foreach (var row in _rows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        _rows.Clear();
        var windows = _controller.Refresh();
        var managed = _controller.Managed().ToDictionary(m => m.Handle);
        foreach (var w in windows)
        {
            managed.TryGetValue(w.Handle, out var m);
            var role = _pending.TryGetValue(w.Handle, out var p) ? p : m is null ? SceneRole.None : RoleOf(m);
            var row = new WindowRow(w, role)
            {
                State = StateOf(m) + (m is not null && _controller.IsDisplayMissing(m.Display) ? " (monitor desconectado)" : ""),
                Display = _pendingDisplay.TryGetValue(w.Handle, out var pd) ? pd : DisplayOption.FromModel(m?.Display),
            };
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        foreach (var gone in _pending.Keys.Except(windows.Select(w => w.Handle)).ToList())
        {
            _pending.Remove(gone);
        }

        foreach (var gone in _pendingDisplay.Keys.Except(windows.Select(w => w.Handle)).ToList())
        {
            _pendingDisplay.Remove(gone);
        }

        WindowList.SelectedItem = _rows.FirstOrDefault(r => r.Snapshot.Handle == selected);
    }

    /// <summary>Un selector de template por panel: el principal admite Tiles y Pila; los auxiliares, además Fill.</summary>
    private void BuildTemplates(IReadOnlyList<FocusWShell.Core.Displays.DisplayInfo> aux)
    {
        _buildingTemplates = true;
        TemplatesPanel.Children.Clear();
        TemplatesPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Templates:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        void Add(string? display, string label, Home home, bool allowFill)
        {
            var options = new List<(PanelTemplate Template, string Label)> { (PanelTemplate.Tiles, "Tiles"), (PanelTemplate.Stack, "Pila") };
            if (allowFill)
            {
                options.Add((PanelTemplate.Fill, "Fill (maximizada)"));
            }

            var combo = new System.Windows.Controls.ComboBox
            {
                Width = 140,
                Margin = new Thickness(0, 0, 16, 0),
                ItemsSource = options.Select(o => o.Label).ToList(),
                SelectedIndex = Math.Max(0, options.FindIndex(o => o.Template == _controller.TemplateOf(display, home))),
            };
            System.Windows.Automation.AutomationProperties.SetName(combo, $"Template de {label}");
            combo.SelectionChanged += async (_, _) =>
            {
                if (_buildingTemplates || combo.SelectedIndex < 0)
                {
                    return;
                }

                var template = options[combo.SelectedIndex].Template;
                await Run(() => _controller.Execute(new SetTemplate(display, home, template)));
                Refresh();
            };
            TemplatesPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            TemplatesPanel.Children.Add(combo);
        }

        Add(null, "Principal · Contexto", Home.Context, allowFill: false);
        Add(null, "Principal · Trabajo", Home.Work, allowFill: false);
        foreach (var a in aux)
        {
            string name = a.FriendlyName ?? a.DeviceName.TrimStart('\\', '.');
            Add(a.Key, $"{name} · Contexto", Home.Context, allowFill: true);
            Add(a.Key, $"{name} · Trabajo", Home.Work, allowFill: true);
        }

        _buildingTemplates = false;
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WindowRow changed && e.PropertyName == nameof(WindowRow.Display))
        {
            _pendingDisplay[changed.Snapshot.Handle] = changed.Display;
            return;
        }

        if (sender is not WindowRow row || e.PropertyName != nameof(WindowRow.Role))
        {
            return;
        }

        _pending[row.Snapshot.Handle] = row.Role;

        // El Centro aloja una sola ventana: la que lo tenía vuelve a su hogar.
        if (row.Role == SceneRole.Center)
        {
            var managed = _controller.Managed().ToDictionary(m => m.Handle);
            foreach (var other in _rows.Where(r => r != row && r.Role == SceneRole.Center))
            {
                other.Role = managed.TryGetValue(other.Snapshot.Handle, out var m)
                    ? (m.Home == Home.Context ? SceneRole.Context : SceneRole.Work)
                    : SceneRole.None;
            }
        }
    }

    /// <summary>Traduce los roles editados a comandos: liberar, adoptar o cambiar de panel, y promover al final.</summary>
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
            var current = m is null ? SceneRole.None : RoleOf(m);
            var display = DisplayOption.ToModel(row.Display);
            if (row.Role == current)
            {
                // Mismo rol: quizá cambió el monitor del panel.
                if (m is not null && m.Display != display && row.Role != SceneRole.None)
                {
                    moves.Add(new SendToDisplay(h, display));
                }

                continue;
            }

            switch (row.Role)
            {
                case SceneRole.None:
                    releases.Add(new Release(h));
                    break;
                case SceneRole.Center:
                    // El Centro está siempre en el principal; el monitor elegido es adonde vuelve.
                    if (m is null)
                    {
                        moves.Add(new Adopt(h, row.App, Home.Work, display));
                    }
                    else if (m.Display != display)
                    {
                        moves.Add(new SendToDisplay(h, display));
                    }

                    promote = new Promote(h);
                    break;
                default:
                    var home = row.Role == SceneRole.Context ? Home.Context : Home.Work;
                    if (m is null)
                    {
                        moves.Add(new Adopt(h, row.App, home, display));
                    }
                    else
                    {
                        if (m.Placement == Placement.Center)
                        {
                            moves.Add(new ReturnToPanel(h));
                        }

                        moves.Add(new SendTo(h, home));
                        if (m.Display != display)
                        {
                            moves.Add(new SendToDisplay(h, display));
                        }
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
        _pendingDisplay.Clear();
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
        _pendingDisplay.Clear();
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
        Command[] commands = managed ? [new Promote(h)] : [new Adopt(h, row.App, Home.Work), new Promote(h)];
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
        var buttons = new[] { ApplyButton, RestoreButton, RefreshButton, PromoteButton, SwapButton, ReturnButton, ParkButton, UnparkButton, ReattachButton, ReleaseButton };
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
        _pendingDisplay.Clear();
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

        var layout = report.Layout.Principal;
        sb.AppendLine(
            CultureInfo.CurrentCulture,
            $"Principal: Contexto {layout.LeftColumn.Width} px · Centro {layout.CenterColumn.Width} px · Trabajo {layout.RightColumn.Width} px");
        var names = FocusWShell.Win32.DisplayEnumerator.Enumerate().ToDictionary(d => d.Key, d => d.FriendlyName ?? d.DeviceName);
        foreach (var aux in report.Layout.Aux)
        {
            sb.AppendLine(
                CultureInfo.CurrentCulture,
                $"{names.GetValueOrDefault(aux.Display, aux.Display)}: {string.Join(" · ", aux.Columns.Select(c => $"{c.Key} {c.Value.Width} px ({aux.EffectiveTemplates[c.Key]})"))}");
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
