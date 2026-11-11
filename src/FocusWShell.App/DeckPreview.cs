using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.App;

/// <summary>
/// <c>FocusWShell.exe --deck-preview &lt;carpeta&gt;</c>: dibuja el deck con datos de ejemplo en sus tres
/// tamaños (tablet entera, media tablet, esquina del principal) y guarda un PNG de cada uno. Sirve para
/// revisar el diseño sin tocar ventanas ni cerrar la instancia abierta.
/// </summary>
public static class DeckPreview
{
    public static void Run(string folder, string? language = null)
    {
        if (language is not null)
        {
            Localization.Apply(language);
        }

        Directory.CreateDirectory(folder);
        var sizes = new[] { ("tablet-entera", 1280, 672), ("media-tablet", 640, 672), ("esquina", 640, 980) };
        foreach (var (name, width, height) in sizes)
        {
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}.png"));
            Save(Sample(manyApps: true), width, height, Path.Combine(folder, $"deck-{name}-muchas.png"));
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-hp.png"), "hp");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-tablet.png"), "tablet");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-mover-elegir.png"), mode: "mover-elegir");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-mover.png"), mode: "mover");
            Save(Sample(manyApps: false) with { Free = [] }, width, height, Path.Combine(folder, $"deck-{name}-sin-libres.png"));
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-cerrar.png"), mode: "cerrar");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-energia.png"), mode: "energia");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-anclar.png"), mode: "anclar");
            // Al frente sin ser la del foco: una libre (Docker) y una de otro monitor (Grafana, en el HP).
            Save(Sample(manyApps: false) with { Foreground = 50 }, width, height, Path.Combine(folder, $"deck-{name}-al-frente-libre.png"));
            Save(Sample(manyApps: false) with { Foreground = 7 }, width, height, Path.Combine(folder, $"deck-{name}-al-frente-hp.png"));
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-energia-seguro.png"), mode: "energia-seguro");
        }
    }

    private static void Save(DeckState state, int width, int height, string path, string? tab = null, string? mode = null)
    {
        var actions = new DeckActions(
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => { },
            () => { },
            () => false,
            () => { },
            () => true,
            () => Task.CompletedTask,
            _ => { },
            () => { },
            _ => { },
            () => null,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => { });
        var deck = new DeckWindow(actions) { Left = -20000, Top = -20000 };
        deck.ShowTab(tab);
        deck.ShowMode(mode, state);
        deck.Present(state, new PixelRect(-20000, -20000, -20000 + width, -20000 + height));
        deck.Width = width;
        deck.Height = height;
        deck.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)deck.Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        deck.Close();
    }

    private static readonly string[] FreeNames = ["Docker Desktop", "Microsoft Edge", "Microsoft Edge", "netbird-ui", "Slack", "Spotify", "WhatsApp"];

    /// <summary>Un ejecutable que siempre existe, para que el ejemplo tenga iconos.</summary>
    private static readonly string SampleExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static DeckApp App(string name, nint? handle, ListItemState state, string panel, bool pinned = false) =>
        new(name, name.ToLowerInvariant() + ".exe", handle, state, handle is null ? "" : $"{name} — ventana", panel,
            new Core.Domain.CatalogApp(name.ToLowerInvariant() + ".exe", name, SampleExe, null, panel, false, DateTimeOffset.Now, DateTimeOffset.Now) { Pinned = pinned });

    private static DeckState Sample(bool manyApps)
    {
        var work = new List<DeckApp>
        {
            App("Google Chrome", 1, ListItemState.InPanel, "trabajo", pinned: true),
            App("Claude", 2, ListItemState.InFocus, "trabajo", pinned: true),
            App("Notepad", null, ListItemState.Closed, "trabajo", pinned: true),
            App("Paint", null, ListItemState.Closed, "trabajo"),
        };
        if (manyApps)
        {
            for (int i = 0; i < 14; i++)
            {
                work.Add(App($"App {i + 1}", 100 + i, ListItemState.InPanel, "trabajo"));
            }
        }

        // Con muchas apps, también muchas libres (para ver que LIBRES no recorta nada).
        var free = (manyApps ? FreeNames.Concat(FreeNames).Concat(FreeNames).ToArray() : FreeNames)
            .Select((n, i) => App(n, 50 + i, ListItemState.InPanel, null!) with { PanelId = null })
            .ToList();
        var panels = new List<DeckPanel>
        {
            new("contexto", "Contexto", "principal, izquierda", [App("Google Chrome", 3, ListItemState.InPanel, "contexto")]) { Template = PanelTemplate.Tiles },
            new("trabajo", "Trabajo", "principal, derecha", work) { Template = PanelTemplate.Tiles },
            new("monitoreo", "Monitoreo", "HP 24fw, izquierda", [App("Grafana", 7, ListItemState.InPanel, "monitoreo"), App("Logs", 8, ListItemState.InPanel, "monitoreo")]) { Display = "hp", Template = PanelTemplate.Tiles },
            new("chat", "Chat", "HP 24fw, derecha", [App("Slack", 9, ListItemState.InPanel, "chat")]) { Display = "hp", Template = PanelTemplate.Stack },
        };
        return new DeckState(
            App("Claude", 2, ListItemState.InFocus, "trabajo"),
            true,
            FocusPosition.Center,
            panels,
            free,
            manyApps ? ["Código", "Lectura", "Reuniones", "Correo"] : ["Código", "Lectura"],
            [(null, "Principal"), ("hp", "HP 24fw"), ("tablet", "Lenovo tablet")]);
    }
}
