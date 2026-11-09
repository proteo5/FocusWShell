using System.Drawing;
using FocusWShell.Win32;
using Forms = System.Windows.Forms;

namespace FocusWShell.App;

/// <summary>Icono del área de notificación y su menú.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _normal;
    private readonly Icon _awake;
    private readonly Forms.ToolStripMenuItem _keepAwakeItem;

    public TrayIcon(Action open, Action toggleKeepAwake, Action restoreAll, Action exit)
    {
        _normal = DrawIcon(Color.FromArgb(40, 120, 220));
        _awake = DrawIcon(Color.FromArgb(235, 160, 30));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir FocusWShell", null, (_, _) => open());
        _keepAwakeItem = new Forms.ToolStripMenuItem("Mantener la pantalla encendida (Ctrl+Alt+Mayús+P)", null, (_, _) => toggleKeepAwake());
        menu.Items.Add(_keepAwakeItem);
        menu.Items.Add("Restaurar todas (Ctrl+Alt+Mayús+Retroceso)", null, (_, _) => restoreAll());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir (devuelve las ventanas a su sitio)", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _normal,
            Text = "FocusWShell",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => open();
    }

    /// <summary>Refleja en el menú y en el icono si la pantalla se mantiene encendida.</summary>
    public void ShowKeepAwake(bool active)
    {
        _keepAwakeItem.Checked = active;
        _icon.Icon = active ? _awake : _normal;
        _icon.Text = active ? "FocusWShell · pantalla siempre encendida" : "FocusWShell";
    }

    public void Notify(string title, string text, Forms.ToolTipIcon kind = Forms.ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(4000, title, text, kind);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _normal.Dispose();
        _awake.Dispose();
    }

    /// <summary>Tres columnas con la central destacada: Contexto | Centro | Trabajo.</summary>
    private static Icon DrawIcon(Color centerColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var side = new SolidBrush(Color.FromArgb(150, 160, 175));
            using var center = new SolidBrush(centerColor);
            g.FillRectangle(side, 1, 4, 7, 24);
            g.FillRectangle(center, 10, 2, 12, 28);
            g.FillRectangle(side, 24, 4, 7, 24);
        }

        nint handle = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle no es dueño del HICON: se clona y se libera el original.
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            IconHandle.Destroy(handle);
        }
    }
}
