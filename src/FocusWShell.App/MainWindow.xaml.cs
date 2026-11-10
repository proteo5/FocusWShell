using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using FocusWShell.Core.Domain;
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

    public MainWindow(ShellController controller)
    {
        _controller = controller;
        InitializeComponent();
        WindowList.ItemsSource = _rows;
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
        { Placement: Placement.Center } => $"Centro (hogar: {(m.Home == Home.Context ? "Contexto" : "Trabajo")})",
        { Home: Home.Context } => "En Contexto",
        _ => "En Trabajo",
    };

    public void Refresh()
    {
        var display = ShellController.TargetDisplay();
        HeaderText.Text =
            $"Monitor {display.DeviceName} · área de trabajo {display.WorkArea} · {display.Dpi} DPI   |   " +
            "Atajos Ctrl+Alt+Mayús: ↑ al Centro · ← anterior · ↓ Centro al panel · Retroceso restaurar todas";

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
            var row = new WindowRow(w, role) { State = StateOf(m) };
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        foreach (var gone in _pending.Keys.Except(windows.Select(w => w.Handle)).ToList())
        {
            _pending.Remove(gone);
        }

        WindowList.SelectedItem = _rows.FirstOrDefault(r => r.Snapshot.Handle == selected);
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
            if (row.Role == current)
            {
                continue;
            }

            switch (row.Role)
            {
                case SceneRole.None:
                    releases.Add(new Release(h));
                    break;
                case SceneRole.Center:
                    if (m is null)
                    {
                        moves.Add(new Adopt(h, row.App, Home.Work));
                    }

                    promote = new Promote(h);
                    break;
                default:
                    var home = row.Role == SceneRole.Context ? Home.Context : Home.Work;
                    if (m is null)
                    {
                        moves.Add(new Adopt(h, row.App, home));
                    }
                    else
                    {
                        if (m.Placement == Placement.Center)
                        {
                            moves.Add(new ReturnToPanel(h));
                        }

                        moves.Add(new SendTo(h, home));
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
        var buttons = new[] { ApplyButton, RestoreButton, RefreshButton, PromoteButton, SwapButton, ReturnButton, ReleaseButton };
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

        var layout = report.Layout;
        sb.AppendLine(
            CultureInfo.CurrentCulture,
            $"Contexto {layout.LeftColumn.Width} px · Centro {layout.CenterColumn.Width} px · Trabajo {layout.RightColumn.Width} px");
        foreach (var note in layout.Notes)
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
