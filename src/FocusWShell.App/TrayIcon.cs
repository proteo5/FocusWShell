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
    private readonly Forms.ToolStripMenuItem _scenesItem = new("Escenas");
    private readonly Forms.ToolStripMenuItem _restoreItem;
    private Func<int, string> _sceneChord = n => "";

    public TrayIcon(Action open, Action toggleKeepAwake, Action restoreAll, Action exit, Action toggleDeck)
    {
        _normal = DrawIcon(Color.FromArgb(40, 120, 220));
        _awake = DrawIcon(Color.FromArgb(235, 160, 30));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir FocusWShell", null, (_, _) => open());
        menu.Items.Add("Mostrar u ocultar el deck", null, (_, _) => toggleDeck());
        menu.Items.Add(_scenesItem);
        _keepAwakeItem = new Forms.ToolStripMenuItem("Mantener la pantalla encendida", null, (_, _) => toggleKeepAwake());
        menu.Items.Add(_keepAwakeItem);
        _restoreItem = new Forms.ToolStripMenuItem("Restaurar todas", null, (_, _) => restoreAll());
        menu.Items.Add(_restoreItem);
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

    /// <summary>Pone en el menú los atajos en uso (configurables).</summary>
    public void SetHotkeys(string keepAwake, string restoreAll, Func<int, string> scene)
    {
        _keepAwakeItem.Text = keepAwake.Length > 0 ? $"Mantener la pantalla encendida ({keepAwake})" : "Mantener la pantalla encendida";
        _restoreItem.Text = restoreAll.Length > 0 ? $"Restaurar todas ({restoreAll})" : "Restaurar todas";
        _sceneChord = scene;
    }

    /// <summary>Rehace el submenú de escenas: las nueve primeras con su atajo.</summary>
    public void ShowScenes(IReadOnlyList<string> names, Action<string> apply)
    {
        ArgumentNullException.ThrowIfNull(names);
        _scenesItem.DropDownItems.Clear();
        if (names.Count == 0)
        {
            _scenesItem.DropDownItems.Add(new Forms.ToolStripMenuItem("(ninguna guardada)") { Enabled = false });
            return;
        }

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            string chord = i < 9 ? _sceneChord(i + 1) : "";
            string text = chord.Length > 0 ? $"{name}\t({chord})" : name;
            _scenesItem.DropDownItems.Add(text, null, (_, _) => apply(name));
        }
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
