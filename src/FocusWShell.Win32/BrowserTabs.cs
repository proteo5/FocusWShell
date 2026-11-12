using System.Runtime.InteropServices;

using System.Windows.Automation;
using FocusWShell.Core.Browsers;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FocusWShell.Win32;

/// <summary>Una pestaña de un navegador.</summary>
/// <param name="Index">Su posición en la barra cuando se leyó (para encontrarla otra vez).</param>
/// <param name="Name">Su nombre accesible tal cual (título más estado y grupo).</param>
/// <param name="Title">El título limpio (<see cref="TabTitle"/>).</param>
/// <param name="Active">Si es la visible en su ventana.</param>
public sealed record BrowserTab(int Index, string Name, string Title, string? Group, bool Active);

/// <summary>
/// Las pestañas de Chrome y Edge con UI Automation (spike S12), sin extensión: leerlas, llevar una al frente y
/// cerrarla. Depende de cómo cada navegador expone su barra, que no es un contrato: si no se encuentra, no hay
/// pestañas y nada más falla. Se llama desde un hilo de fondo, nunca desde el de la UI.
/// </summary>
public static class BrowserTabs
{
    private static readonly Condition TabItems = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

    /// <summary>Si la ventana es de un navegador cuyas pestañas se pueden leer (Chrome o Edge).</summary>
    public static bool IsBrowser(string processName, string className) =>
        className == "Chrome_WidgetWin_1"
        && (processName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) || processName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Las pestañas de la ventana, en orden; <c>null</c> si no tiene barra de pestañas (p. ej. una app web).</summary>
    public static IReadOnlyList<BrowserTab>? Read(nint handle)
    {
        try
        {
            if (Items(handle) is not { } items)
            {
                return null;
            }

            var tabs = new List<BrowserTab>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var name = items[i].Current.Name;
                var clean = TabTitle.Clean(name);
                tabs.Add(new BrowserTab(i, name, clean.Title, clean.Group, IsSelected(items[i])));
            }

            return tabs;
        }
        catch (Exception ex) when (IsGone(ex))
        {
            return null;
        }
    }

    /// <summary>La hace visible en su ventana. No activa la ventana (eso lo decide quien llama).</summary>
    public static bool Select(nint handle, BrowserTab tab)
    {
        try
        {
            if (Find(handle, tab) is not { } element || !element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
            {
                return false;
            }

            ((SelectionItemPattern)pattern).Select();
            return true;
        }
        catch (Exception ex) when (IsGone(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// La cierra con un clic central enviado como mensaje: no cambia la pestaña activa, no trae la ventana al
    /// frente ni mueve el ratón. Una ventana minimizada no atiende clics, así que se muestra un instante sin
    /// activarla y se vuelve a minimizar. Quien llama debe silenciar el observador de ventanas mientras tanto.
    /// </summary>
    public static unsafe bool Close(nint handle, BrowserTab tab)
    {
        var hwnd = new HWND((void*)handle);
        try
        {
            if (Find(handle, tab) is not { } element)
            {
                return false;
            }

            bool minimized = PInvoke.IsIconic(hwnd);
            if (minimized)
            {
                PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
                Thread.Sleep(200);
            }

            try
            {
                if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll))
                {
                    ((ScrollItemPattern)scroll).ScrollIntoView();
                }

                var rect = element.Current.BoundingRectangle;
                if (rect.IsEmpty || rect.Width <= 0 || double.IsInfinity(rect.Width))
                {
                    return false;
                }

                var point = new System.Drawing.Point((int)(rect.X + (rect.Width / 2)), (int)(rect.Y + (rect.Height / 2)));
                PInvoke.ScreenToClient(hwnd, ref point);
                var position = new LPARAM((point.Y << 16) | (point.X & 0xFFFF));
                PInvoke.PostMessage(hwnd, 0x0207 /* WM_MBUTTONDOWN */, new WPARAM(0x0010 /* MK_MBUTTON */), position);
                PInvoke.PostMessage(hwnd, 0x0208 /* WM_MBUTTONUP */, default, position);
                return true;
            }
            finally
            {
                if (minimized)
                {
                    Thread.Sleep(150);
                    PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE);
                }
            }
        }
        catch (Exception ex) when (IsGone(ex))
        {
            return false;
        }
    }

    /// <summary>La pestaña otra vez, por su título y la posición más cercana a la que tenía (la lista pudo cambiar).</summary>
    private static AutomationElement? Find(nint handle, BrowserTab tab)
    {
        if (Items(handle) is not { } items)
        {
            return null;
        }

        return Enumerable.Range(0, items.Count)
            .Where(i => TabTitle.Clean(items[i].Current.Name).Title == tab.Title)
            .OrderBy(i => Math.Abs(i - tab.Index))
            .Select(i => items[i])
            .FirstOrDefault();
    }

    private static List<AutomationElement>? Items(nint handle)
    {
        var window = AutomationElement.FromHandle(handle);
        if (Strip(window, 0) is not { } strip)
        {
            return null;
        }

        return [.. strip.FindAll(TreeScope.Descendants, TabItems).Cast<AutomationElement>()];
    }

    /// <summary>La barra de pestañas: el primer control <c>Tab</c>, sin entrar en el contenido web.</summary>
    private static AutomationElement? Strip(AutomationElement element, int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        var walker = TreeWalker.ControlViewWalker;
        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            var type = child.Current.ControlType;
            if (type == ControlType.Tab)
            {
                return child;
            }

            if (type != ControlType.Document && Strip(child, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsSelected(AutomationElement element) =>
        element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected;

    /// <summary>La ventana o la pestaña dejaron de existir, o el navegador no respondió.</summary>
    private static bool IsGone(Exception ex) =>
        ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException or COMException or TimeoutException;
}
