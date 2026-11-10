using FocusWShell.Core.Domain;

namespace FocusWShell.App;

/// <summary>Crea, actualiza y cierra las tiras de pestañas según el layout. Sólo en el hilo de UI.</summary>
public sealed class StripManager(ShellController controller, Action<ControllerResult> onResult)
{
    private readonly Dictionary<string, PanelStripWindow> _windows = [];

    public void Refresh()
    {
        var strips = controller.Strips();
        foreach (var key in _windows.Keys.Except(strips.Select(s => s.Key)).ToList())
        {
            _windows[key].Close();
            _windows.Remove(key);
        }

        foreach (var strip in strips)
        {
            if (!_windows.TryGetValue(strip.Key, out var window))
            {
                window = new PanelStripWindow(strip, Run);
                _windows[strip.Key] = window;
            }

            window.Update(strip);
        }
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private async Task Run(Command command)
    {
        var result = await Task.Run(() => controller.Execute(command));
        onResult(result);
    }
}
