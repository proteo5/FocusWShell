using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Text;
using System.Windows;
using FocusWShell.Win32;
using FocusWShell.Win32.Persistence;
using FocusWShell.Win32.Scenes;

namespace FocusWShell.App;

/// <summary>Lista de ventanas, asignación de roles, aplicar y restaurar (alcance de M0).</summary>
public partial class MainWindow : Window
{
    private readonly ShellController _controller;
    private readonly ObservableCollection<WindowRow> _rows = [];

    /// <summary>Roles elegidos por el usuario, por handle; sobreviven a "Actualizar lista".</summary>
    private readonly Dictionary<nint, SceneRole> _roles = [];

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

    public void Refresh()
    {
        var display = ShellController.TargetDisplay();
        HeaderText.Text =
            $"Monitor {display.DeviceName} · área de trabajo {display.WorkArea} · {display.Dpi} DPI   |   " +
            $"Emergencia: {EmergencyHotkey.Description}";

        foreach (var row in _rows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        _rows.Clear();
        var windows = _controller.ListManageable();
        foreach (var w in windows)
        {
            var row = new WindowRow(w, _roles.GetValueOrDefault(w.Handle));
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        // Olvida roles de ventanas que ya no existen.
        foreach (var gone in _roles.Keys.Except(windows.Select(w => w.Handle)).ToList())
        {
            _roles.Remove(gone);
        }

        int pending = _controller.PendingCount;
        SetStatus(pending == 0
            ? $"{windows.Count} ventanas gestionables. Ninguna está movida por FocusWShell."
            : $"{windows.Count} ventanas gestionables. {pending} están fuera de su sitio original (en el diario).");
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WindowRow row || e.PropertyName != nameof(WindowRow.Role))
        {
            return;
        }

        _roles[row.Snapshot.Handle] = row.Role;

        // El Centro aloja una sola ventana: la anterior pierde el rol.
        if (row.Role == SceneRole.Center)
        {
            foreach (var other in _rows.Where(r => r != row && r.Role == SceneRole.Center))
            {
                other.Role = SceneRole.None;
            }
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var assignment = new SceneAssignment(
            _rows.Where(r => r.Role == SceneRole.Context).Select(r => r.Snapshot).ToList(),
            _rows.FirstOrDefault(r => r.Role == SceneRole.Center)?.Snapshot,
            _rows.Where(r => r.Role == SceneRole.Work).Select(r => r.Snapshot).ToList());

        if (!assignment.All.Any())
        {
            SetStatus("Asigna al menos una ventana a Contexto, Centro o Trabajo. Para devolverlas todas, usa Restaurar todas.");
            return;
        }

        await RunBusy(async () =>
        {
            var (released, applied) = await Task.Run(() => _controller.Apply(assignment));
            SetStatus(Describe(released, applied));
        });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        await RunBusy(async () =>
        {
            var report = await Task.Run(_controller.RestoreAll);
            SetStatus(report.Entries.Count == 0 ? "No había nada que restaurar." : Describe(report));
        });
    }

    private async Task RunBusy(Func<Task> action)
    {
        ApplyButton.IsEnabled = RestoreButton.IsEnabled = RefreshButton.IsEnabled = false;
        try
        {
            await action();
        }
        finally
        {
            ApplyButton.IsEnabled = RestoreButton.IsEnabled = RefreshButton.IsEnabled = true;
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

    private static string Describe(RestoreReport released, SceneApplyReport report)
    {
        var sb = new StringBuilder();
        if (released.Entries.Count > 0)
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"Devueltas a su sitio por quedarse sin rol: {released.Summary}");
        }

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
        sb.AppendLine(report.Summary);
        foreach (var e in report.Entries.Where(e => e.Outcome is RestoreOutcome.Pending or RestoreOutcome.RestoredWithDifferences))
        {
            sb.AppendLine(CultureInfo.CurrentCulture, $"  ! {e.Entry.ProcessName}: {e.Detail}");
        }

        return sb.ToString();
    }
}
