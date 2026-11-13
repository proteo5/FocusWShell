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
            // Disposición del principal (ADR 0012, enmienda): Trabajo apagado en un ultrawide (sale «⇠ Trabajo»),
            // un monitor normal con sólo Trabajo y Solo foco.
            Save(Arranged(FocusPosition.ContextOnly, ultrawide: true), width, height, Path.Combine(folder, $"deck-{name}-solo-contexto.png"));
            Save(Arranged(FocusPosition.Right, ultrawide: true), width, height, Path.Combine(folder, $"deck-{name}-trabajo-prestado.png"));
            Save(Arranged(FocusPosition.WorkOnly, ultrawide: false), width, height, Path.Combine(folder, $"deck-{name}-monitor-normal.png"));
            Save(Arranged(FocusPosition.FocusOnly, ultrawide: true), width, height, Path.Combine(folder, $"deck-{name}-solo-foco.png"));
            // Al frente sin ser la del foco: una libre (Docker) y una de otro monitor (Grafana, en el HP).
            Save(Sample(manyApps: false) with { Foreground = 50 }, width, height, Path.Combine(folder, $"deck-{name}-al-frente-libre.png"));
            Save(Sample(manyApps: false) with { Foreground = 7 }, width, height, Path.Combine(folder, $"deck-{name}-al-frente-hp.png"));
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-energia-seguro.png"), mode: "energia-seguro");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-paginas.png"), mode: "paginas");
            Save(Sample(manyApps: false), width, height, Path.Combine(folder, $"deck-{name}-paginas-sitio.png"), mode: "paginas-sitio");
            // Un solo monitor: la fila de pestañas sale sólo por «Páginas».
            Save(Sample(manyApps: false) with { Displays = [(null, "Principal")] }, width, height, Path.Combine(folder, $"deck-{name}-un-monitor.png"));
        }
    }

    private static void Save(DeckState state, int width, int height, string path, string? tab = null, string? mode = null)
    {
        var actions = new DeckActions(
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
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
            () => Task.CompletedTask,
            (_, _) => { },
            ReadPages: () => Task.FromResult(SamplePages()),
            SelectPage: (_, _) => Task.FromResult(true),
            ClosePage: (_, _) => Task.FromResult(true));
        var deck = new DeckWindow(actions) { Left = -20000, Top = -20000 };
        deck.ShowTab(tab);
        deck.ShowMode(mode, state);
        if (mode is "paginas" or "paginas-sitio")
        {
            deck.ShowPages(SamplePages(), mode == "paginas-sitio" ? "Jira" : null);
        }

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

    private static readonly string[] FreeNames = ["Docker Desktop", "Microsoft Edge", "Microsoft Edge", "OneNote", "Slack", "Spotify", "WhatsApp"];

    /// <summary>
    /// El ejemplo en el idioma de la vista previa: las capturas del README y del manual salen de aquí, en
    /// inglés y en español. Son datos inventados, no de nadie.
    /// </summary>
    private static string T(string es, string en) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? en : es;

    /// <summary>Un ejecutable que siempre existe, para que el ejemplo tenga iconos.</summary>
    private static readonly string SampleExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static DeckApp App(string name, nint? handle, ListItemState state, string panel, bool pinned = false) =>
        new(name, name.ToLowerInvariant() + ".exe", handle, state, handle is null ? "" : $"{name} — {T("ventana", "window")}", panel,
            new Core.Domain.CatalogApp(name.ToLowerInvariant() + ".exe", name, SampleExe, null, panel, false, DateTimeOffset.Now, DateTimeOffset.Now) { Pinned = pinned });

    private static DeckState Sample(bool manyApps)
    {
        var work = new List<DeckApp>
        {
            App("Google Chrome", 1, ListItemState.InPanel, "trabajo", pinned: true),
            App("Claude", 2, ListItemState.InFocus, "trabajo", pinned: true),
            App("Notepad", null, ListItemState.Closed, "trabajo", pinned: true),
            App("Notes", null, ListItemState.OtherDesktop, "trabajo"),
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
            new("contexto", T("Contexto", "Context"), T("principal, izquierda", "main, left"), [App("Google Chrome", 3, ListItemState.InPanel, "contexto")]) { Template = PanelTemplate.Tiles, Side = PanelSide.Left },
            new("trabajo", T("Trabajo", "Work"), T("principal, derecha", "main, right"), work) { Template = PanelTemplate.Tiles, Side = PanelSide.Right },
            new("monitoreo", T("Monitoreo", "Monitoring"), T("HP 24fw, izquierda", "HP 24fw, left"), [App("Grafana", 7, ListItemState.InPanel, "monitoreo"), App("Logs", 8, ListItemState.InPanel, "monitoreo")]) { Display = "hp", Template = PanelTemplate.Tiles },
            new("chat", "Chat", T("HP 24fw, derecha", "HP 24fw, right"), [App("Slack", 9, ListItemState.InPanel, "chat"), App("Teams", 10, ListItemState.InPanel, "chat")]) { Display = "hp", Template = PanelTemplate.Stack },
        };
        return new DeckState(
            App("Claude", 2, ListItemState.InFocus, "trabajo"),
            true,
            FocusPosition.Center,
            panels,
            free,
            [(null, T("Principal", "Main")), ("hp", "HP 24fw"), ("tablet", "Lenovo tablet")])
        {
            Browsers = new HashSet<nint> { 1, 3 },
            Ultrawide = true,
        };
    }

    /// <summary>El ejemplo con otra disposición del principal: sus paneles de las columnas apagadas, atenuados.</summary>
    private static DeckState Arranged(FocusPosition view, bool ultrawide)
    {
        var state = Sample(manyApps: false);
        bool Off(DeckPanel p) => p.Display is null && (p.Side == PanelSide.Left
            ? view is FocusPosition.WorkOnly or FocusPosition.FocusOnly
            : view is FocusPosition.ContextOnly or FocusPosition.FocusOnly);
        return state with
        {
            View = view,
            Panels = [.. state.Panels.Select(p => Off(p) ? p with { Where = p.Where + T(" · apagado", " · off"), Off = true } : p)],
            Ultrawide = ultrawide,
        };
    }

    /// <summary>Páginas de ejemplo: un Chrome en el foco y dos Edge, uno con grupos.</summary>
    private static IReadOnlyList<PageWindow> SamplePages()
    {
        Win32.BrowserTab Tab(int i, string title, string? group = null, bool active = false) => new(i, title, title, group, active);
        Core.Domain.CatalogApp Browser(string name) =>
            new(name.ToLowerInvariant() + ".exe", name, SampleExe, null, "trabajo", false, DateTimeOffset.Now, DateTimeOffset.Now);
        return
        [
            new PageWindow(1, "Google Chrome", T("en el foco", "in focus"), true, true, false, Browser("Google Chrome"),
            [
                Tab(0, "Inbox (2) - ana@example.com - Gmail"),
                Tab(1, "Sprint board - Jira"),
                Tab(2, "Backlog - Jira"),
                Tab(3, "Release 2.4 - Roadmap - Jira", active: true),
                Tab(4, "DNS | Records | example.com | Cloudflare"),
                Tab(5, "Security | WAF | example.com | Cloudflare"),
                Tab(6, T("Calendario - Semana del 9 de noviembre", "Calendar - Week of November 9")),
                Tab(7, "Free SPF Checker & SPF Record Lookup - Instant Results"),
            ]),
            new PageWindow(3, "Microsoft Edge", T("Contexto", "Context"), false, true, false, Browser("Microsoft Edge"),
            [
                Tab(0, "Wikipedia", T("Lectura", "Reading")),
                Tab(1, "Outlook", "Email"),
                Tab(2, "Lo-fi beats to code to - YouTube", "Music", active: true),
                Tab(3, "Jazz for a rainy day - YouTube", "Music"),
                Tab(4, "YouTube", "Music"),
                Tab(5, "Bugs - List - Jira"),
            ]),
            new PageWindow(50, "Microsoft Edge", T("libre", "free"), false, false, false, Browser("Microsoft Edge"),
            [
                Tab(0, "Fix login by ana · Pull Request #15 · acme/web", active: true),
                Tab(1, "acme/api: The API server"),
            ]),
        ];
    }
}
