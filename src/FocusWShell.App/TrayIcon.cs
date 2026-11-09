using System.Drawing;
using FocusWShell.Win32;
using Forms = System.Windows.Forms;

namespace FocusWShell.App;

/// <summary>Icono del área de notificación con el menú de M0.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(Action open, Action restoreAll, Action exit)
    {
        _image = DrawIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir FocusWShell", null, (_, _) => open());
        menu.Items.Add($"Restaurar todas ({EmergencyHotkey.Description})", null, (_, _) => restoreAll());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir (devuelve las ventanas a su sitio)", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "FocusWShell",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => open();
    }

    public void Notify(string title, string text, Forms.ToolTipIcon kind = Forms.ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(4000, title, text, kind);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>Tres columnas con la central destacada: Contexto | Centro | Trabajo.</summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var side = new SolidBrush(Color.FromArgb(150, 160, 175));
            using var center = new SolidBrush(Color.FromArgb(40, 120, 220));
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
