using FocusWShell.Core.Domain;

namespace FocusWShell.App;

/// <summary>Crea, actualiza y cierra las listas tipo Explorador según el layout (ADR 0015). Sólo en el hilo de UI.</summary>
public sealed class ListManager(ShellController controller, Action<ControllerResult> onResult, Action<string> onMessage)
{
    private readonly Dictionary<string, ListPanelWindow> _windows = [];

    public void Refresh()
    {
        var views = controller.ListPanels();
        foreach (var key in _windows.Keys.Except(views.Select(v => v.Key)).ToList())
        {
            _windows[key].Close();
            _windows.Remove(key);
        }

        foreach (var view in views)
        {
            if (!_windows.TryGetValue(view.Key, out var window))
            {
                window = new ListPanelWindow(view, Run, OpenApp);
                _windows[view.Key] = window;
            }

            window.Update(view);
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
        Refresh();
    }

    private async Task OpenApp(string appKey, string panelId)
    {
        onMessage(await Task.Run(() => controller.OpenApp(appKey, panelId)));
        Refresh();
    }
}
