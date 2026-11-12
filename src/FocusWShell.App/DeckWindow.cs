using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusWShell.Core.Browsers;
using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;
using FocusWShell.Win32;

namespace FocusWShell.App;

/// <summary>Lo que el deck necesita de la app para actuar.</summary>
public sealed record DeckActions(
    Func<Command[], Task> Run,
    Func<string, string, Task> OpenApp,
    Action ToggleKeepAwake,
    Func<bool> KeepAwake,
    Action FreeAll,
    Func<bool> CanRearrange,
    Func<Task> RearrangeAll,
    Action<DeckPower> Power,
    Action Hide,
    Action<DeckDock?> Dock,
    Func<DeckDock?> CurrentDock,
    Func<nint, Task> CloseApp,
    Func<nint, Task> BringToFront,
    Func<Task> MouseToFocus,
    Action<string, bool> SetPinned,
    Func<Task<IReadOnlyList<PageWindow>>>? ReadPages = null,
    Func<nint, BrowserTab, Task<bool>>? SelectPage = null,
    Func<nint, BrowserTab, Task<bool>>? ClosePage = null);

/// <summary>Lo que hace el panel de energía del deck.</summary>
public enum DeckPower
{
    Lock,
    Sleep,
    Restart,
    ShutDown,
    SignOut,
}

/// <summary>
/// El deck (ADR 0013, fase 2): una superficie de control en una esquina del monitor principal. Muestra el
/// foco, las vistas, cada panel con sus apps (abiertas a color, cerradas apagadas) y las ventanas libres;
/// tocar una app la lleva al foco (o la abre) y el deck se oculta solo. No roba el foco (S8), y sus botones
/// miden al menos 48 px para el dedo.
/// </summary>
public sealed class DeckWindow : Window
{
    private static readonly Brush Background0 = Frozen(Color.FromRgb(0x14, 0x17, 0x1E));
    private static readonly Brush CardBrush = Frozen(Color.FromRgb(0x24, 0x29, 0x33));
    private static readonly Brush ActiveBrush = Frozen(Color.FromRgb(0x28, 0x78, 0xDC));
    private static readonly Brush WarnBrush = Frozen(Color.FromRgb(0xC2, 0x3B, 0x3B));
    private static readonly Brush AwakeBrush = Frozen(Color.FromRgb(0xC8, 0x8A, 0x1E));

    /// <summary>Anclar: índigo, distinto del azul de lo activo. Sólido, anclada; tenue, se puede anclar.</summary>
    private static readonly Brush PinBrush = Frozen(Color.FromRgb(0x5A, 0x78, 0xC8));

    private static readonly Brush PinFaintBrush = Frozen(Color.FromArgb(0x70, 0x5A, 0x78, 0xC8));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xE6, 0xE9, 0xEF));
    private static readonly Brush DimBrush = Frozen(Color.FromRgb(0x8A, 0x93, 0xA2));

    private const double Touch = 48;

    private readonly DeckActions _actions;
    private readonly Grid _root = new() { Margin = new Thickness(10, 10, 2, 2), Background = Background0 };
    private DeckState? _state;

    /// <summary>"Restaurar todas" pide un segundo toque antes de actuar.</summary>
    private DateTime _restoreArmed = DateTime.MinValue;

    public DeckWindow(DeckActions actions)
    {
        _actions = actions;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Background = Background0;
        Title = "FocusWShell · deck";
        Content = _root; // sin scroll: todo cabe (ver Render)
        SizeChanged += (_, _) =>
        {
            if (_state is not null && _renderedWide != IsWide)
            {
                Render();
            }
        };
        SourceInitialized += (_, _) => OwnWindows.MakeToolNoActivate(new WindowInteropHelper(this).Handle);

        // Al ocultarse, se olvida si se estaba moviendo una app: al volver, el deck empieza limpio.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _moving = null;
                _moveApp = null;
                _closing = false;
                _pinning = false;
                _powerOpen = false;
                _powerArmed = null;
            }
        };
    }

    /// <summary>Lo muestra (o lo actualiza) en el rectángulo dado, en píxeles físicos.</summary>
    public void Present(DeckState state, PixelRect where)
    {
        _state = state;
        Render();
        if (!IsVisible)
        {
            Show();
        }

        OwnWindows.Place(new WindowInteropHelper(this).EnsureHandle(), where);
    }

    /// <summary>
    /// Cambió la ventana al frente: sólo se redibuja si cambia qué se marca (así un toque en curso no se
    /// pierde por redibujar a cada cambio de ventana).
    /// </summary>
    public void UpdateForeground(nint hwnd)
    {
        if (_state is not { } state)
        {
            return;
        }

        var before = ForegroundMark(state);
        _state = state with { Foreground = hwnd };
        if (IsVisible && ForegroundMark(_state) != before)
        {
            Render();
        }
    }

    /// <summary>
    /// La app que se marca como «al frente»: la que recibe el teclado, sólo si no es la del foco (en el caso
    /// normal no se marca nada) y si está en el deck (un panel o las libres).
    /// </summary>
    private static nint? ForegroundMark(DeckState state) =>
        state.Foreground is { } fg && fg != state.Focus?.Handle
            && state.Panels.SelectMany(p => p.Apps).Concat(state.Free).Any(a => a.Handle == fg)
            ? fg
            : null;

    /// <summary>Si hay un modo encendido: entonces mandan sus bordes, no el de «al frente».</summary>
    private bool InMode => _moving is not null || _closing || _pinning;

    /// <summary>Actualiza el contenido si está visible.</summary>
    public void Update(DeckState state)
    {
        _state = state;
        if (IsVisible)
        {
            Render();
        }
    }

    /// <summary>A partir de este ancho (en DIP) el deck usa dos columnas: controles a la izquierda y paneles a la derecha.</summary>
    private const double WideFrom = 900;

    private bool? _renderedWide;

    private bool IsWide => ActualWidth >= WideFrom;

    /// <summary>
    /// Sin scroll: todo cabe en el deck. Ancho (la tablet entera): controles a la izquierda y paneles en
    /// columnas a la derecha, con las libres abajo. Estrecho (media tablet, esquina del principal): filas
    /// compactas. Las apps de cada panel se encogen para caber (<see cref="FitGrid"/>).
    /// </summary>
    private void Render()
    {
        var state = _state!;
        DropStaleMove(state);
        _renderedWide = IsWide;
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        if (IsWide)
        {
            RenderWide(state);
        }
        else
        {
            RenderNarrow(state);
        }
    }

    /// <summary>Si la app elegida para mover ya no está (se cerró), se vuelve a elegir.</summary>
    private void DropStaleMove(DeckState state)
    {
        if (_moveApp is { Handle: { } chosen } && !state.Panels.SelectMany(p => p.Apps).Concat(state.Free).Any(a => a.Handle == chosen))
        {
            _moveApp = null;
            _moving = MoveStep.PickApp;
        }
    }

    private void RenderWide(DeckState state)
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Izquierda, en tres tarjetas separadas por más aire que sus botones entre sí:
        //   Foco (la app y qué hacer con ella), Disposición (las columnas del principal) y, abajo, Sistema.
        var left = new Grid();
        void Card(Border card, GridLength height)
        {
            left.RowDefinitions.Add(new RowDefinition { Height = height });
            Grid.SetRow(card, left.RowDefinitions.Count - 1);
            left.Children.Add(card);
        }

        // Mover, anclar y cerrar en una fila de tres: con sus textos cortos, para que quepan.
        var actions = FocusActions(state, shortText: true);
        // Arriba las de acción inmediata, abajo las de modo, con un hueco que separa los dos tipos.
        var modes = Buttons(3, actions.Skip(3));
        modes.Margin = new Thickness(0, 8, 0, 2);
        Card(Group(null, [(FocusHeader(state), GridLength.Auto), (Buttons(3, actions.Take(3)), GridLength.Auto), (modes, GridLength.Auto)]), GridLength.Auto);

        // La disposición del principal: las columnas de paneles que se prenden y apagan, como un mapa del
        // monitor (ADR 0012, enmienda). El foco ocupa lo que dejan.
        var arrangement = ArrangementButtons(state, compact: false);
        var layout = new List<(UIElement, GridLength)>
        {
            (Label(Strings.Deck_SecView), GridLength.Auto),
            (Buttons(arrangement.Count, arrangement), GridLength.Auto),
        };

        Card(Group(null, layout), GridLength.Auto);
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // aire hasta Sistema
        var system = Group(_powerOpen ? Strings.Deck_SecPower : Strings.Deck_SecSystem, [(Buttons(2, SystemButtons()), GridLength.Auto)]);
        system.Margin = new Thickness(0, 0, 0, 8); // abajo, alineada con LIBRES
        Card(system, GridLength.Auto);
        Grid.SetColumn(left, 0);
        _root.Children.Add(left);

        // Derecha: arriba las pestañas de los monitores; debajo, sus paneles en columnas (dos filas si son
        // más de tres) y las libres abajo.
        var right = new Grid();
        if (TabRow(state, wide: true) is { } tabBar)
        {
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.Children.Add(tabBar);
        }

        int panelsRow = right.RowDefinitions.Count;
        // LIBRES va siempre, aunque esté vacía: el deck no cambia de forma y se ve que es un destino.
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        // Con muchas libres, LIBRES crece (hasta un tercio del alto); siempre cabe al menos una fila entera.
        double freeWeight = state.Free.Count == 0 ? 0.5 : Math.Clamp(0.75 + (state.Free.Count / 16.0), 1, 1.5);
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(freeWeight, GridUnitType.Star), MinHeight = 104 });

        var shown = PanelsOnTab(state);
        var panels = new Grid();
        int perRow = shown.Count <= 3 ? Math.Max(1, shown.Count) : (int)Math.Ceiling(shown.Count / 2.0);
        int rows = (int)Math.Ceiling(shown.Count / (double)perRow);
        for (int c = 0; c < perRow; c++)
        {
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int r = 0; r < Math.Max(1, rows); r++)
        {
            panels.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        if (shown.Count == 0)
        {
            panels.Children.Add(NoPanelsCard(state));
        }

        for (int i = 0; i < shown.Count; i++)
        {
            var card = PanelCard(shown[i]);
            Grid.SetColumn(card, i % perRow);
            Grid.SetRow(card, i / perRow);
            panels.Children.Add(card);
        }

        // Con «Páginas» elegida, la lista de páginas ocupa la zona de los paneles.
        UIElement area = _pagesOpen ? PagesView() : panels;
        Grid.SetRow(area, panelsRow);
        right.Children.Add(area);
        var free = FreeCard(state);
        Grid.SetRow(free, panelsRow + 1);
        right.Children.Add(free);

        Grid.SetColumn(right, 2);
        _root.Children.Add(right);
    }

    private void RenderNarrow(DeckState state)
    {
        void Row(UIElement e, GridLength height, double min = 0)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = height, MinHeight = min });
            Grid.SetRow(e, _root.RowDefinitions.Count - 1);
            _root.Children.Add(e);
        }

        Row(FocusHeader(state, arrangement: true), GridLength.Auto);
        // Las seis en una fila: un hueco tras «Ratón» separa las de acción inmediata de las de modo.
        var focusActions = FocusActions(state, shortText: true);
        var focusRow = Buttons(6, focusActions);
        focusActions[2].Margin = new Thickness(0, 0, 20, 8);
        Row(focusRow, GridLength.Auto);

        // Pestañas por monitor y, debajo, sus paneles uno debajo de otro, con alto según cuántas apps tienen.
        if (TabRow(state, wide: false) is { } tabRow)
        {
            Row(tabRow, GridLength.Auto);
        }

        var shown = PanelsOnTab(state);
        if (_pagesOpen)
        {
            // Ocupa lo que ocuparían los paneles de ese monitor.
            Row(PagesView(), new GridLength(Math.Max(2, shown.Sum(p => 1 + p.Apps.Count)), GridUnitType.Star), 116);
        }
        else
        {
            if (shown.Count == 0)
            {
                Row(NoPanelsCard(state), new GridLength(1, GridUnitType.Star), 116);
            }

            foreach (var panel in shown)
            {
                Row(PanelCard(panel), new GridLength(1 + panel.Apps.Count, GridUnitType.Star), 116);
            }
        }

        // Con «Páginas» elegida, la lista se queda con el sitio de LIBRES y de las escenas: aquí no sobra.
        if (!_pagesOpen)
        {
            Row(FreeCard(state), new GridLength(1 + (state.Free.Count / 2.0), GridUnitType.Star), state.Free.Count > 0 ? 90 : 60);
        }

        // Abajo, lo del sistema.
        var system = SystemButtons();
        Row(Buttons(system.Count > 4 ? 3 : system.Count, system), GridLength.Auto);
    }

    // --- Pestañas por monitor -------------------------------------------------------------------------

    /// <summary>Un monitor como pestaña del deck.</summary>
    /// <param name="Display">Su clave; <c>null</c>: el principal.</param>
    /// <param name="Apps">Apps abiertas en sus paneles.</param>
    private sealed record MonitorTab(string? Display, string Name, int Apps, bool Connected);

    /// <summary>Pestaña elegida en esta sesión: <c>""</c> el principal; <c>null</c>, ninguna aún (va el principal).</summary>
    private string? _tab;

    private static string TabKey(string? display) => display ?? "";

    /// <summary>Para la vista previa: «mover-elegir», «mover» (con la primera app de Trabajo elegida) o «cerrar».</summary>
    internal void ShowMode(string? mode, DeckState state)
    {
        if (mode == "mover-elegir")
        {
            _moving = MoveStep.PickApp;
        }

        if (mode == "mover")
        {
            _moving = MoveStep.PickPanel;
            _moveApp = state.Panels.SelectMany(p => p.Apps).FirstOrDefault(a => a.PanelId == "trabajo" && a.Handle is not null);
        }

        _closing = mode == "cerrar";
        _pinning = mode == "anclar";
        _powerOpen = mode is "energia" or "energia-seguro";
        if (mode == "energia-seguro")
        {
            _powerArmed = (DeckPower.ShutDown, DateTime.Now.AddHours(1));
        }
    }

    /// <summary>Elige la pestaña de un monitor (<c>null</c>: el principal). Para la vista previa.</summary>
    internal void ShowTab(string? display) => _tab = TabKey(display);

    private bool OnTab(string? display) => string.Equals(TabKey(display), _tab ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Los monitores conectados y los que tienen paneles aunque se hayan desconectado. No sale el monitor
    /// donde el deck ocupa toda la pantalla: ahí no caben paneles.
    /// </summary>
    private List<MonitorTab> MonitorTabs(DeckState state)
    {
        int OpenApps(string? display) => state.Panels
            .Where(p => string.Equals(TabKey(p.Display), TabKey(display), StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Apps.Count(a => a.Handle is not null));
        var whole = _actions.CurrentDock() is { Zone: DeckZone.Whole } dock ? dock.Display : null;
        var tabs = state.Displays
            .Where(d => d.Display is null || !string.Equals(d.Display, whole, StringComparison.OrdinalIgnoreCase))
            .Select(d => new MonitorTab(d.Display, d.Name, OpenApps(d.Display), true))
            .ToList();
        foreach (var missing in state.Panels.Select(p => p.Display).Where(d => d is not null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!tabs.Any(t => string.Equals(TabKey(t.Display), TabKey(missing), StringComparison.OrdinalIgnoreCase)))
            {
                var name = state.Panels.First(p => string.Equals(p.Display, missing, StringComparison.OrdinalIgnoreCase)).Where.Split(',')[0];
                tabs.Add(new MonitorTab(missing, name, OpenApps(missing), false));
            }
        }

        // Si la pestaña elegida ya no existe (p. ej. el deck pasó a ocupar ese monitor), vuelve el principal.
        if (_tab is not null && !tabs.Any(t => OnTab(t.Display)))
        {
            _tab = null;
        }

        return tabs;
    }

    /// <summary>Los paneles del monitor de la pestaña elegida.</summary>
    private List<DeckPanel> PanelsOnTab(DeckState state)
    {
        MonitorTabs(state); // corrige la pestaña si ya no existe
        return state.Panels.Where(p => OnTab(p.Display)).ToList();
    }

    private Button TabButton(MonitorTab tab)
    {
        string text = tab.Connected ? $"{tab.Name} · {tab.Apps}" : Core.Text.F(Strings.Deck_TabDisconnected, tab.Name);
        var b = WideButton("🖵  " + text, true, () =>
        {
            _tab = TabKey(tab.Display);
            _pagesOpen = false;
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(b, Core.Text.F(Strings.Deck_TabName, tab.Name));
        if (OnTab(tab.Display) && !_pagesOpen)
        {
            b.Background = ActiveBrush;
        }
        else if (!InMode && _state is { } current && ForegroundMark(current) is { } front
            && current.Panels.Any(p => string.Equals(TabKey(p.Display), TabKey(tab.Display), StringComparison.OrdinalIgnoreCase) && p.Apps.Any(a => a.Handle == front)))
        {
            // La app al frente está en otro monitor: su pestaña lo dice, aunque no se esté viendo.
            b.BorderBrush = TextBrush;
            b.BorderThickness = new Thickness(2);
        }
        else
        {
            if (!tab.Connected)
            {
                b.Foreground = DimBrush;
            }

            // Moviendo una app: los otros monitores también son destino. Tocarlo cambia de pestaña sin salir
            // del modo mover, y ahí se elige el panel.
            if (_moving == MoveStep.PickPanel)
            {
                b.BorderBrush = TargetBrush;
                b.BorderThickness = new Thickness(3);
                b.Foreground = TextBrush;
            }
        }

        // Con el ratón: al pasar una app arrastrada por encima, se cambia a ese monitor para soltarla allí.
        b.AllowDrop = true;
        b.DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DragFormat) && (!OnTab(tab.Display) || _pagesOpen))
            {
                _tab = TabKey(tab.Display);
                _pagesOpen = false;
                Dispatcher.BeginInvoke(Render);
            }
        };
        return b;
    }

    /// <summary>
    /// La fila de pestañas: los monitores (si hay más de uno) y, a la derecha y aparte, «Páginas» si hay
    /// Chrome o Edge abiertos. Las dos eligen qué se ve en la zona de los paneles.
    /// </summary>
    private DockPanel? TabRow(DeckState state, bool wide)
    {
        var tabs = MonitorTabs(state);
        bool pages = _actions.ReadPages is not null && (state.Browsers.Count > 0 || _pagesOpen);
        if (tabs.Count <= 1 && !pages)
        {
            return null;
        }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2), LastChildFill = true };
        if (pages)
        {
            var button = PagesTabButton(wide);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }

        var monitors = new UniformGrid { Rows = 1 };
        foreach (var tab in tabs)
        {
            var b = TabButton(tab);
            if (wide)
            {
                StretchOut(b);
            }

            monitors.Children.Add(b);
        }

        row.Children.Add(monitors);
        return row;
    }

    // --- Páginas de los navegadores (spike S12) -----------------------------------------------------

    /// <summary>«Páginas» elegida: la zona de los paneles muestra las pestañas de todos los navegadores.</summary>
    private bool _pagesOpen;

    /// <summary>Lo último leído (<c>null</c>: leyendo por primera vez).</summary>
    private IReadOnlyList<PageWindow>? _pages;

    /// <summary>Cuántas páginas había la última vez que se leyeron (para la pestaña, sin leer en cada repaso).</summary>
    private int? _pagesCount;

    /// <summary>Dónde estaba el desplazamiento, para conservarlo cuando el deck se redibuja.</summary>
    private double _pagesOffset;

    private Button PagesTabButton(bool wide)
    {
        string text = _pagesCount is { } count ? Core.Text.F(Strings.Deck_PagesTabCount, count) : Strings.Deck_PagesTab;
        var b = WideButton(wide ? "🗂  " + text : "🗂", true, () => _pagesOpen ? ClosePages() : OpenPages());
        b.MinWidth = wide ? 150 : Touch;
        b.Width = wide ? double.NaN : Touch;
        b.Padding = new Thickness(wide ? 12 : 0, 0, wide ? 12 : 0, 0);
        // Un hueco la separa de los monitores: no es un monitor.
        b.Margin = new Thickness(wide ? 12 : 10, 0, 0, wide ? 8 : 6);
        b.ToolTip = Strings.Deck_PagesTabName;
        System.Windows.Automation.AutomationProperties.SetName(b, Strings.Deck_PagesTabName);
        if (_pagesOpen)
        {
            b.Background = ActiveBrush;
        }

        return b;
    }

    /// <summary>Elige «Páginas» (en sus sitios) y lee los navegadores.</summary>
    private async Task OpenPages()
    {
        _pagesOpen = true;
        _pagesSite = null;
        _closeAllArmed = null;
        _pagesOffset = 0;
        Render();
        await LoadPages();
    }

    private Task ClosePages()
    {
        _pagesOpen = false;
        Render();
        return Task.CompletedTask;
    }

    private async Task LoadPages()
    {
        if (_actions.ReadPages is not { } read)
        {
            return;
        }

        var pages = await read();
        _pages = pages;
        _pagesCount = pages.Sum(w => w.Tabs.Count);
        if (_pagesOpen)
        {
            Render();
        }
    }

    /// <summary>Para la vista previa: «Páginas» elegida, con estas páginas.</summary>
    internal void ShowPages(IReadOnlyList<PageWindow> pages, string? site = null)
    {
        _pagesOpen = true;
        _pagesSite = site;
        _pages = pages;
        _pagesCount = pages.Sum(w => w.Tabs.Count);
    }

    /// <summary>Una página con su ventana y el sitio al que pertenece (<c>null</c>: suelta).</summary>
    private sealed record PageEntry(PageWindow Window, BrowserTab Tab, string? Site);

    /// <summary>Sitio elegido en el segundo paso; <c>null</c>: el primero (sitios y sueltas).</summary>
    private string? _pagesSite;

    /// <summary>«Cerrar todas» armado: un segundo toque antes de esta hora las cierra.</summary>
    private DateTime? _closeAllArmed;

    private List<PageEntry> PageEntries()
    {
        var all = (_pages ?? []).SelectMany(w => w.Tabs.Select(t => (Window: w, Tab: t))).ToList();
        var sites = PageSites.Assign([.. all.Select(x => x.Tab.Title)]);
        return [.. all.Select((x, i) => new PageEntry(x.Window, x.Tab, sites[i]))];
    }

    /// <summary>
    /// Las páginas de todos los navegadores en dos pasos: primero los sitios con varias páginas y las páginas
    /// sueltas; tocar un sitio muestra sus páginas, y tocar una suelta la abre directo. Sin búsqueda: se
    /// desplaza con el dedo.
    /// </summary>
    private Border PagesView()
    {
        var card = new Border
        {
            BorderBrush = CardBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 8, 8),
        };
        var entries = PageEntries();
        if (entries.Count == 0)
        {
            card.Child = new TextBlock
            {
                Text = _pages is null ? Strings.Deck_PagesLoading : Strings.Deck_PagesNone,
                Foreground = DimBrush,
                Margin = new Thickness(6),
                TextWrapping = TextWrapping.Wrap,
            };
            return card;
        }

        var inSite = _pagesSite is { } site ? entries.Where(e => e.Site == site).ToList() : [];
        if (_pagesSite is not null && inSite.Count == 0)
        {
            _pagesSite = null; // se cerraron todas: de vuelta a los sitios
        }

        var body = new DockPanel();
        if (_pagesSite is not null)
        {
            var header = SiteHeader(_pagesSite, inSite);
            DockPanel.SetDock(header, Dock.Top);
            body.Children.Add(header);
        }

        var list = new StackPanel();
        if (_pagesSite is null)
        {
            SitesAndLoose(list, entries);
        }
        else
        {
            foreach (var entry in inSite)
            {
                list.Children.Add(PageRow(entry.Window, entry.Tab, PageSites.ShortTitle(entry.Tab.Title, _pagesSite)));
            }
        }

        var scroller = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        DragToScroll(scroller);
        scroller.Loaded += (_, _) => scroller.ScrollToVerticalOffset(_pagesOffset);
        scroller.ScrollChanged += (_, e) => _pagesOffset = e.VerticalOffset;
        body.Children.Add(scroller);
        card.Child = body;
        return card;
    }

    /// <summary>Paso 1: los sitios con varias páginas (de más a menos) y, debajo, las sueltas por título.</summary>
    private void SitesAndLoose(StackPanel list, List<PageEntry> entries)
    {
        int columns = IsWide ? 4 : 2;
        var sites = entries.Where(e => e.Site is not null).GroupBy(e => e.Site!)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (sites.Count > 0)
        {
            list.Children.Add(Label(Strings.Deck_PagesSites));
            var grid = new UniformGrid { Columns = columns };
            foreach (var group in sites)
            {
                grid.Children.Add(SiteTile(group.Key, group.Count()));
            }

            list.Children.Add(grid);
        }

        var loose = entries.Where(e => e.Site is null).OrderBy(e => e.Tab.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (loose.Count > 0)
        {
            list.Children.Add(Label(Strings.Deck_PagesLoose));
            // Una columna menos que los sitios: cada suelta lleva su ✕ y el título necesita sitio.
            var grid = new UniformGrid { Columns = IsWide ? 3 : 2 };
            foreach (var entry in loose)
            {
                grid.Children.Add(LooseTile(entry));
            }

            list.Children.Add(grid);
        }
    }

    /// <summary>Un sitio: su nombre y cuántas páginas tiene. Tocarlo muestra sus páginas.</summary>
    private Button SiteTile(string site, int count)
    {
        var content = new DockPanel();
        var number = new TextBlock { Text = $"{count} ›", Foreground = DimBrush, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(number, Dock.Right);
        content.Children.Add(number);
        content.Children.Add(new TextBlock { Text = site, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var tile = PageButton(content);
        System.Windows.Automation.AutomationProperties.SetName(tile, Core.Text.F(Strings.Deck_SiteName, site, count));
        tile.Click += (_, _) =>
        {
            _pagesSite = site;
            _pagesOffset = 0;
            _closeAllArmed = null;
            Render();
        };
        return tile;
    }

    /// <summary>Una página suelta: su título (tocarlo la abre) y ✕ para cerrarla.</summary>
    private Grid LooseTile(PageEntry entry)
    {
        var tile = new Grid { Margin = new Thickness(0, 0, 4, 4) };
        tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Touch + 2) });
        var go = PageButton(new TextBlock { Text = entry.Tab.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        go.Margin = new Thickness(0);
        Mark(go, entry.Window, entry.Tab);
        go.ToolTip = entry.Tab.Title + Environment.NewLine + Core.Text.F(Strings.Deck_PageWhere, entry.Window.Browser, entry.Window.Where);
        System.Windows.Automation.AutomationProperties.SetName(go, Core.Text.F(Strings.Deck_PageName, entry.Tab.Title));
        go.Click += async (_, _) => await ChoosePage(entry.Window, entry.Tab);
        tile.Children.Add(go);
        var close = CloseButton(entry.Tab.Title, () => ClosePages([entry]));
        Grid.SetColumn(close, 1);
        tile.Children.Add(close);
        return tile;
    }

    /// <summary>✕ de 48 para cerrar una página. Rojo: cerrar, como el resto del deck.</summary>
    private static Button CloseButton(string title, Func<Task> close)
    {
        var button = new Button
        {
            Content = "✕",
            Width = Touch,
            Height = Touch,
            Margin = new Thickness(2, 0, 0, 0),
            Focusable = false,
            Foreground = WarnBrush,
            Background = CardBrush,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 14,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, Core.Text.F(Strings.Deck_PageCloseName, title));
        button.Click += async (_, _) => await close();
        return button;
    }

    /// <summary>Paso 2, arriba: volver a los sitios, cuál es, y «Cerrar todas» (con un segundo toque).</summary>
    private DockPanel SiteHeader(string site, List<PageEntry> pages)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        var back = WideButton(Strings.Deck_PagesBack, true, () =>
        {
            _pagesSite = null;
            _pagesOffset = 0;
            _closeAllArmed = null;
            Render();
        });
        back.MinWidth = 0;
        back.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(back, Dock.Left);
        header.Children.Add(back);

        bool armed = _closeAllArmed is { } until && until > DateTime.Now;
        var closeAll = WideButton(armed ? Core.Text.F(Strings.Deck_PagesCloseAllConfirm, pages.Count) : Strings.Deck_PagesCloseAll, true, async () =>
        {
            if (_closeAllArmed is { } deadline && deadline > DateTime.Now)
            {
                _closeAllArmed = null;
                await ClosePages(pages);
                return;
            }

            // Primer toque: se arma y, si no se confirma en 4 s, vuelve a su texto.
            _closeAllArmed = DateTime.Now.AddSeconds(4);
            Render();
            await Task.Delay(4100);
            if (_closeAllArmed is { } still && still <= DateTime.Now)
            {
                _closeAllArmed = null;
                Render();
            }
        });
        closeAll.MinWidth = 0;
        closeAll.Margin = new Thickness(8, 0, 0, 0);
        closeAll.Foreground = armed ? TextBrush : WarnBrush;
        closeAll.Background = armed ? WarnBrush : CardBrush;
        DockPanel.SetDock(closeAll, Dock.Right);
        header.Children.Add(closeAll);

        header.Children.Add(new TextBlock
        {
            Text = Core.Text.F(Strings.Deck_PagesSite, site, pages.Count),
            Foreground = TextBrush,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return header;
    }

    /// <summary>Un botón de página o sitio: 48 de alto, texto a la izquierda.</summary>
    private static Button PageButton(UIElement content) => new()
    {
        Content = content,
        Height = Touch,
        Margin = new Thickness(0, 0, 4, 4),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(12, 0, 10, 0),
        Focusable = false,
        Foreground = TextBrush,
        Background = CardBrush,
        BorderBrush = ActiveBrush,
        BorderThickness = new Thickness(0),
        Cursor = Cursors.Hand,
    };

    /// <summary>La activa de su ventana, con la barra azul; las de otro escritorio, apagadas.</summary>
    private static void Mark(Button button, PageWindow window, BrowserTab tab)
    {
        if (tab.Active)
        {
            button.BorderThickness = new Thickness(4, 0, 0, 0);
            button.Padding = new Thickness(8, 0, 10, 0);
        }

        if (window.OtherDesktop)
        {
            button.Foreground = DimBrush;
        }
    }

    /// <summary>
    /// Arrastrar con el dedo desplaza la lista. Los toques de la tablet llegan como ratón, y el desplazamiento
    /// táctil de WPF no responde a eso; además cada fila es un botón y se quedaba con el arrastre. Así que se
    /// hace a mano: pasados 10 px, la lista se lleva el arrastre y el botón donde empezó no se pulsa. Un
    /// toque corto sigue siendo un toque. La barra de desplazamiento funciona como siempre.
    /// </summary>
    private static void DragToScroll(ScrollViewer scroller)
    {
        Point? start = null;
        double startOffset = 0;
        bool dragging = false;
        scroller.PreviewMouseLeftButtonDown += (_, e) =>
        {
            start = IsInScrollBar(e.OriginalSource as DependencyObject) ? null : e.GetPosition(scroller);
            startOffset = scroller.VerticalOffset;
            dragging = false;
        };
        scroller.PreviewMouseMove += (_, e) =>
        {
            if (start is not { } origin || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            double dy = e.GetPosition(scroller).Y - origin.Y;
            if (!dragging && Math.Abs(dy) > 10)
            {
                dragging = true;
                scroller.CaptureMouse(); // el botón pierde la captura: no habrá clic
            }

            if (dragging)
            {
                scroller.ScrollToVerticalOffset(startOffset - dy);
                e.Handled = true;
            }
        };
        scroller.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (dragging)
            {
                e.Handled = true;
                scroller.ReleaseMouseCapture();
            }

            start = null;
            dragging = false;
        };
        scroller.LostMouseCapture += (_, _) =>
        {
            if (dragging)
            {
                start = null;
                dragging = false;
            }
        };
    }

    private static bool IsInScrollBar(DependencyObject? element)
    {
        for (var e = element; e is not null; e = e is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
        {
            if (e is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Paso 2: una página del sitio, sin el nombre del sitio, con su ventana al lado y ✕ para cerrarla.</summary>
    private Grid PageRow(PageWindow window, BrowserTab tab, string title)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Touch + 2) });
        var content = new DockPanel();
        var where = new TextBlock
        {
            Text = Core.Text.F(Strings.Deck_PageWhere, window.Browser, window.Where),
            Foreground = DimBrush,
            FontSize = 11,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(where, Dock.Right);
        content.Children.Add(where);
        content.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var go = PageButton(content);
        go.Margin = new Thickness(0);
        Mark(go, window, tab);
        go.ToolTip = tab.Title;
        System.Windows.Automation.AutomationProperties.SetName(go, Core.Text.F(Strings.Deck_PageName, tab.Title));
        go.Click += async (_, _) => await ChoosePage(window, tab);
        row.Children.Add(go);

        var close = CloseButton(tab.Title, () => ClosePages([new PageEntry(window, tab, _pagesSite)]));
        Grid.SetColumn(close, 1);
        row.Children.Add(close);
        return row;
    }

    /// <summary>
    /// Lleva la página al frente, como tocar su app: una ventana de un panel va al foco; una libre (o de otro
    /// escritorio) sólo se trae al frente. Después se hace visible la pestaña.
    /// </summary>
    private async Task ChoosePage(PageWindow window, BrowserTab tab)
    {
        _pagesOpen = false;
        if (window.Managed && !window.InFocus && !window.OtherDesktop)
        {
            await Run(true, new Promote(window.Handle));
        }
        else
        {
            if (!Docked)
            {
                _actions.Hide();
            }

            await _actions.BringToFront(window.Handle);
        }

        if (_actions.SelectPage is { } select)
        {
            await select(window.Handle, tab);
        }

        Render();
    }

    /// <summary>
    /// Cierra páginas (una, o todas las de un sitio): desaparecen de la lista al momento y, al terminar, se
    /// vuelven a leer los navegadores. Una a una, para que cada navegador las procese en orden.
    /// </summary>
    private async Task ClosePages(IReadOnlyList<PageEntry> entries)
    {
        if (_actions.ClosePage is not { } close || _pages is null || entries.Count == 0)
        {
            return;
        }

        var closing = entries.Select(e => (e.Window.Handle, e.Tab)).ToHashSet();
        _pages = [.. _pages
            .Select(w => w with { Tabs = [.. w.Tabs.Where(t => !closing.Contains((w.Handle, t)))] })
            .Where(w => w.Tabs.Count > 0)];
        _pagesCount = _pages.Sum(w => w.Tabs.Count);
        Render();
        foreach (var entry in entries)
        {
            await close(entry.Window.Handle, entry.Tab);
            await Task.Delay(120);
        }

        await Task.Delay(300);
        await LoadPages();
    }

    /// <summary>Un monitor sin paneles: cómo crearlos.</summary>
    private Border NoPanelsCard(DeckState state)
    {
        var name = MonitorTabs(state).FirstOrDefault(t => OnTab(t.Display))?.Name ?? Strings.Deck_ThisMonitor;
        return new Border
        {
            BorderBrush = CardBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 8, 8),
            Child = new TextBlock
            {
                Text = Core.Text.F(Strings.Deck_NoPanels, name),
                Foreground = DimBrush,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    // --- Piezas ----------------------------------------------------------------------------------------

    private DockPanel FocusHeader(DeckState state, bool arrangement = false)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 8, 8) };
        var close = SmallButton("✕", Strings.Deck_Hide, () => _actions.Hide());
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        // «Enviar a» (no es anclar: el 📌 es de las apps ancladas).
        var send = SmallButton("⇢", Strings.Deck_SendTo, () => { });
        send.Margin = new Thickness(0, 0, 6, 0);
        send.ContextMenu = SendMenu(state);
        send.Click += (_, _) =>
        {
            send.ContextMenu.PlacementTarget = send;
            send.ContextMenu.IsOpen = true;
        };
        DockPanel.SetDock(send, Dock.Right);
        header.Children.Add(send);
        if (arrangement)
        {
            // En el deck estrecho, los interruptores de columnas van en la cabecera, sólo con su icono.
            var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            foreach (var b in ArrangementButtons(state, compact: true))
            {
                b.Margin = new Thickness(0, 0, 2, 0);
                toggles.Children.Add(b);
            }

            DockPanel.SetDock(toggles, Dock.Right);
            header.Children.Add(toggles);
        }
        var focus = new StackPanel();
        focus.Children.Add(Label(Strings.Deck_SecFocus));

        // El icono de la app del foco, a la izquierda de su nombre y su título.
        var line = new DockPanel();
        if (state.Focus?.App is { } focusApp && AppIcons.Get(focusApp) is { } icon)
        {
            var image = new Image
            {
                Source = icon,
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(image, Dock.Left);
            line.Children.Add(image);
        }

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = state.Focus is { } f ? f.Name : Strings.Deck_FocusEmpty,
            Foreground = state.Focus is null ? DimBrush : TextBrush,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (state.Focus is { Title.Length: > 0 } ft)
        {
            names.Children.Add(new TextBlock { Text = ft.Title, Foreground = DimBrush, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        line.Children.Add(names);
        focus.Children.Add(line);

        header.Children.Add(focus);
        return header;
    }

    private List<Button> FocusActions(DeckState state, bool shortText)
    {
        var previous = WideButton("⇄  " + Strings.Deck_Previous, state.HasPrevious, () => Run(true, new SwapWithPrevious()));
        var back = WideButton(shortText ? "↓  " + Strings.Deck_ToPanelShort : "↓  " + Strings.Deck_ToPanel, state.Focus is not null, () => Run(false, new ReturnToPanel(state.Focus!.Handle!.Value)));
        System.Windows.Automation.AutomationProperties.SetName(back, "Deck: " + Strings.Deck_ToPanel);
        var move = WideButton("⇆  " + (_moving is not null ? Strings.Deck_Moving : shortText ? Strings.Deck_MoveShort : Strings.Deck_Move), true, () =>
        {
            _moving = _moving is null ? MoveStep.PickApp : null;
            _moveApp = null;
            _closing = false;
            _pinning = false;
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(move, "Deck: " + Strings.Deck_Move);
        if (_moving is not null)
        {
            move.Background = ActiveBrush;
        }

        var close = WideButton("✕  " + (_closing ? Strings.Deck_Closing : shortText ? Strings.Deck_CloseShort : Strings.Deck_Close), true, () =>
        {
            _closing = !_closing;
            _moving = null;
            _moveApp = null;
            _pinning = false;
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(close, "Deck: " + Strings.Deck_Close);
        if (_closing)
        {
            close.Background = WarnBrush;
        }

        // Anclar: se queda encendido para anclar o desanclar varias; se apaga tocándolo otra vez.
        var pin = WideButton("📌  " + (_pinning ? Strings.Deck_Pinning : Strings.Deck_Pin), true, () =>
        {
            _pinning = !_pinning;
            _moving = null;
            _moveApp = null;
            _closing = false;
            Render();
        });
        System.Windows.Automation.AutomationProperties.SetName(pin, "Deck: " + Strings.Deck_Pin);
        if (_pinning)
        {
            pin.Background = PinBrush;
        }

        // El ratón al foco: tras tocar el deck, el cursor queda en la tablet; así vuelve a donde se trabaja.
        var mouse = WideButton("🖱  " + (shortText ? Strings.Deck_MouseShort : Strings.Deck_Mouse), true, () => _actions.MouseToFocus());
        mouse.ToolTip = Strings.Deck_MouseTip;
        System.Windows.Automation.AutomationProperties.SetName(mouse, "Deck: " + Strings.Deck_Mouse);

        Sticky(move, _moving is not null, TargetBrush);
        Sticky(pin, _pinning, PinBrush);
        Sticky(close, _closing, WarnFaintBrush);
        return [previous, back, mouse, move, pin, close];
    }

    private static readonly Brush WarnFaintBrush = Frozen(Color.FromArgb(0x90, 0xC2, 0x3B, 0x3B));

    /// <summary>
    /// Un botón de modo (se queda encendido esperando el siguiente toque, como Shift) se distingue de uno de
    /// acción inmediata antes de tocarlo: «…» al final, como en Windows («pide algo más»), y una barra abajo en
    /// el color que tomarán las ventanas en ese modo. Encendido, el relleno ya lo dice y el texto ya acaba en «…».
    /// </summary>
    private static void Sticky(Button b, bool on, Brush mode)
    {
        if (on)
        {
            return;
        }

        if (b.Content is string text && !text.EndsWith('…'))
        {
            b.Content = text + "…";
        }

        b.BorderBrush = mode;
        b.BorderThickness = new Thickness(0, 0, 0, 3);
    }

    /// <summary>
    /// Modo anclar: tocar una app de un panel la ancla (cerrada, sigue saliendo en su panel) o la desancla.
    /// Mientras dura, salen también las cerradas sin anclar, para poder anclarlas.
    /// </summary>
    private bool _pinning;

    private static bool IsPinned(DeckApp app) => app.App?.Pinned == true;

    /// <summary>Si se puede anclar: una app del catálogo que está en un panel (las libres no tienen panel).</summary>
    private static bool CanPin(DeckApp app) => app.App is not null && app.PanelId is not null;

    /// <summary>Las apps que se ven de un panel: las cerradas sólo si están ancladas (o en el modo anclar).</summary>
    private List<DeckApp> Visible(IEnumerable<DeckApp> apps) =>
        [.. apps.Where(a => a.Handle is not null || a.State == ListItemState.OtherDesktop || _pinning || IsPinned(a))];

    /// <summary>
    /// No está aquí: cerrada o en otro escritorio (ADR 0016). Se ve apagada, no se mueve ni se cierra, y
    /// tocarla la abre o lleva a su escritorio.
    /// </summary>
    private static bool Away(DeckApp app) => app.State is ListItemState.Closed or ListItemState.OtherDesktop;

    /// <summary>
    /// Modo cerrar: la app que se toque se cierra como con su X (si tiene algo sin guardar, ella pregunta) y
    /// el modo se apaga, como «Mover». También se apaga tocando otra vez el botón.
    /// </summary>
    private bool _closing;

    /// <summary>La app que se acaba de mandar cerrar: su tarjeta dice «Cerrando…» hasta que desaparece.</summary>
    private nint? _closingNow;

    /// <summary>
    /// Los interruptores de la disposición del principal, de izquierda a derecha como en el monitor: el extra de
    /// la izquierda (traer Trabajo, sólo en un ultrawide), Contexto, Trabajo y el extra de la derecha (traer
    /// Contexto). Azul: prendido. Su menú (pulsación larga) restablece el ancho de esta disposición.
    /// </summary>
    private List<Button> ArrangementButtons(DeckState state, bool compact)
    {
        var view = state.View;
        string left = ColumnName(state, PanelSide.Left) ?? Strings.Deck_LeftColumn;
        string right = ColumnName(state, PanelSide.Right) ?? Strings.Deck_RightColumn;
        var buttons = new List<Button>();
        if (MainArrangement.CanBorrowToLeft(view, state.Ultrawide))
        {
            buttons.Add(ArrangementToggle(compact ? "⇠" : "⇠  " + right, Core.Text.F(Strings.Deck_BorrowTip, right),
                MainArrangement.RightBorrowedLeft(view), MainArrangement.ToggleBorrowToLeft(view), state, compact));
        }

        buttons.Add(ArrangementToggle(compact ? "◧" : "◧  " + left, Core.Text.F(Strings.Deck_ColumnTip, left),
            MainArrangement.LeftOn(view), MainArrangement.ToggleLeft(view), state, compact));
        buttons.Add(ArrangementToggle(compact ? "◨" : right + "  ◨", Core.Text.F(Strings.Deck_ColumnTip, right),
            MainArrangement.RightOn(view), MainArrangement.ToggleRight(view), state, compact));
        if (MainArrangement.CanBorrowToRight(view, state.Ultrawide))
        {
            buttons.Add(ArrangementToggle(compact ? "⇢" : left + "  ⇢", Core.Text.F(Strings.Deck_BorrowTip, left),
                MainArrangement.LeftBorrowedRight(view), MainArrangement.ToggleBorrowToRight(view), state, compact));
        }

        return buttons;
    }

    private Button ArrangementToggle(string text, string tip, bool on, FocusPosition next, DeckState state, bool compact)
    {
        var b = WideButton(text, true, () => Run(false, new SetView(next)));
        if (compact)
        {
            b.MinWidth = Touch;
            b.Width = Touch;
            b.Padding = new Thickness(0);
            b.FontSize = 16;
        }
        else
        {
            b.MinWidth = 0;
            b.Padding = new Thickness(8, 0, 8, 0);
        }

        b.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(b, "Deck: " + tip);
        if (on)
        {
            b.Background = ActiveBrush;
        }

        var menu = new ContextMenu();
        var reset = new MenuItem { Header = Strings.Deck_ResetWidth };
        reset.Click += async (_, _) => await Run(false, new SetProportions(state.View, null));
        menu.Items.Add(reset);
        b.ContextMenu = menu;
        return b;
    }

    /// <summary>El nombre de la columna de ese lado en el principal: el de su primer panel.</summary>
    private static string? ColumnName(DeckState state, PanelSide side) =>
        state.Panels.Where(p => p.Display is null && p.Side == side).OrderBy(p => p.Slot).FirstOrDefault()?.Name;

    private Button AwakeButton()
    {
        var awake = WideButton(_actions.KeepAwake() ? "☀  " + Strings.Deck_AwakeOn : "☾  " + Strings.Deck_AwakeOff, true, () =>
        {
            _actions.ToggleKeepAwake();
            Render();
        });
        if (_actions.KeepAwake())
        {
            awake.Background = AwakeBrush;
        }

        return awake;
    }

    private Button RestoreButton()
    {
        bool armed = DateTime.Now - _restoreArmed < TimeSpan.FromSeconds(4);
        var restore = WideButton(armed ? "⟲  " + Strings.Deck_RestoreSure : "⟲  " + Strings.Deck_RestoreAll, true, () =>
        {
            if (DateTime.Now - _restoreArmed < TimeSpan.FromSeconds(4))
            {
                _restoreArmed = DateTime.MinValue;
                _actions.FreeAll();
                if (!Docked)
                {
                    _actions.Hide();
                }
            }
            else
            {
                _restoreArmed = DateTime.Now;
                Render();
            }
        });
        if (armed)
        {
            restore.Background = WarnBrush;
        }

        return restore;
    }

    /// <summary>
    /// Lo del sistema, que se usa poco y va abajo: pantalla encendida, energía, liberar todas y, sólo si
    /// hubo un «Liberar todas», reordenarlas como estaban. Con la energía abierta, sus botones grandes.
    /// </summary>
    private List<Button> SystemButtons()
    {
        if (_powerOpen)
        {
            return PowerButtons();
        }

        var buttons = new List<Button> { AwakeButton(), PowerButton(), RestoreButton() };
        if (_actions.CanRearrange())
        {
            buttons.Add(WideButton("↺  " + Strings.Deck_Rearrange, true, async () =>
            {
                await _actions.RearrangeAll();
                if (!Docked)
                {
                    _actions.Hide();
                }
            }));
        }

        return buttons;
    }

    /// <summary>Si el panel de energía está abierto (en lugar de los botones del sistema).</summary>
    private bool _powerOpen;

    /// <summary>La acción de energía que pidió confirmación y cuándo (un segundo toque en 4 s la hace).</summary>
    private (DeckPower Action, DateTime At)? _powerArmed;

    private Button PowerButton() => WideButton("⏻  " + Strings.Deck_Power, true, () =>
    {
        _powerOpen = true;
        _powerArmed = null;
        Render();
    });

    /// <summary>
    /// Bloquear y suspender, con un toque (se deshacen solos al volver). Reiniciar, apagar y cerrar sesión
    /// piden un segundo toque. Ninguna libera las ventanas: al volver, la sesión las pone en sus paneles.
    /// </summary>
    private List<Button> PowerButtons()
    {
        Button Make(DeckPower action, string icon, string text, bool confirm)
        {
            bool armed = _powerArmed is { } a && a.Action == action && DateTime.Now - a.At < TimeSpan.FromSeconds(4);
            var b = WideButton(icon + "  " + (armed ? Core.Text.F(Strings.Deck_PowerSure, text) : text), true, () =>
            {
                if (confirm && !armed)
                {
                    _powerArmed = (action, DateTime.Now);
                    Render();
                    return;
                }

                _powerOpen = false;
                _powerArmed = null;
                Render();
                _actions.Power(action);
            });
            System.Windows.Automation.AutomationProperties.SetName(b, "Deck: " + text);
            if (armed)
            {
                b.Background = WarnBrush;
            }

            return b;
        }

        var cancel = WideButton("✕  " + Strings.Deck_PowerCancel, true, () =>
        {
            _powerOpen = false;
            _powerArmed = null;
            Render();
        });
        return
        [
            Make(DeckPower.Lock, "🔒", Strings.Deck_PowerLock, confirm: false),
            Make(DeckPower.Sleep, "☾", Strings.Deck_PowerSleep, confirm: false),
            Make(DeckPower.Restart, "↻", Strings.Deck_PowerRestart, confirm: true),
            Make(DeckPower.ShutDown, "⏻", Strings.Deck_PowerShutDown, confirm: true),
            Make(DeckPower.SignOut, "⇥", Strings.Deck_PowerSignOut, confirm: true),
            cancel,
        ];
    }

    /// <summary>
    /// Una tarjeta de la columna izquierda: sus filas dentro de un borde, con su título si lo tiene. Entre
    /// tarjetas hay más aire que entre los botones de una misma tarjeta, para que se lean como grupos.
    /// </summary>
    private static Border Group(string? title, IEnumerable<(UIElement Element, GridLength Height)> rows)
    {
        var grid = new Grid();
        void Add(UIElement e, GridLength height)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
            Grid.SetRow(e, grid.RowDefinitions.Count - 1);
            grid.Children.Add(e);
        }

        if (title is not null)
        {
            Add(Label(title), GridLength.Auto);
        }

        foreach (var (element, height) in rows)
        {
            Add(element, height);
        }

        return new Border
        {
            Child = grid,
            BorderBrush = CardBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 0, 0),
            Margin = new Thickness(0, 0, 0, 12),
        };
    }

    /// <summary>Botones en una fila de columnas iguales que se estiran (sin anchos fijos).</summary>
    private static UniformGrid Buttons(int columns, IEnumerable<Button> buttons)
    {
        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var b in buttons)
        {
            StretchOut(b);
            grid.Children.Add(b);
        }

        return grid;
    }

    private static void StretchOut(Button b)
    {
        b.MinWidth = 0;
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Padding = new Thickness(6, 0, 6, 0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Un panel: su cabecera y sus apps, que se encogen para caber. También es zona donde soltar.</summary>
    private Border PanelCard(DeckPanel panel)
    {
        var body = new DockPanel();
        var header = PanelHeader(panel);
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        var apps = Visible(panel.Apps);
        if (apps.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Deck_PanelEmpty, Foreground = DimBrush, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Top });
        }
        else
        {
            body.Children.Add(AppGrid(apps));
        }

        // Al mover, los paneles donde puede ir la app elegida se ponen amarillos y todo el panel es el
        // destino: un toque en cualquier parte la lleva ahí (más fácil con el dedo que un botón).
        bool target = _moving == MoveStep.PickPanel && _moveApp is { } chosen && chosen.PanelId != panel.Id;
        UIElement content = body;
        if (target)
        {
            content = WithTarget(body, "⬇  " + Strings.Deck_MoveHere, Core.Text.F(Strings.Deck_MoveHereName, _moveApp!.Name, panel.Name), () => MoveChosenTo(panel));
        }

        var zone = DropZone(content, panel);
        if (panel.Off)
        {
            zone.Opacity = 0.55; // su columna está apagada: sus apps, minimizadas (tocarlas las lleva al foco)
        }

        zone.Margin = new Thickness(0, 0, 8, 8);
        zone.BorderBrush = target ? TargetBrush : CardBrush;
        zone.BorderThickness = new Thickness(target ? 3 : 1);
        zone.Padding = new Thickness(target ? 4 : 6);
        return zone;
    }

    /// <summary>Borde de un panel donde se puede soltar la app que se está moviendo.</summary>
    private static readonly Brush TargetBrush = Frozen(Color.FromRgb(0xF2, 0xC1, 0x2E));

    /// <summary>Velo encima de ese panel: se ve que todo él es el destino.</summary>
    private static readonly Brush TargetWash = Frozen(Color.FromArgb(0x55, 0xF2, 0xC1, 0x2E));

    /// <summary>Si la app elegida para mover está en un panel: entonces LIBRES es un destino (liberarla).</summary>
    private bool CanReleaseChosen => _moving == MoveStep.PickPanel && _moveApp is { PanelId: not null };

    /// <summary>
    /// Las ventanas libres. También es un destino: soltar ahí una app (arrastrada, o al moverla con toques)
    /// la libera, y vuelve a su sitio original.
    /// </summary>
    private Border FreeCard(DeckState state)
    {
        var body = new DockPanel();
        var label = Label(Strings.Deck_SecFree);
        DockPanel.SetDock(label, Dock.Top);
        body.Children.Add(label);
        if (state.Free.Count > 0)
        {
            body.Children.Add(AppGrid(state.Free));
        }
        else
        {
            body.Children.Add(new TextBlock { Text = Strings.Deck_NoFree, Foreground = DimBrush, Margin = new Thickness(4) });
        }

        UIElement content = body;
        bool target = CanReleaseChosen;
        if (target)
        {
            content = WithTarget(body, "⬇  " + Strings.Deck_ReleaseHere, Core.Text.F(Strings.Deck_ReleaseName, _moveApp!.Name), ReleaseChosen);
        }

        var card = new Border
        {
            Child = content,
            BorderBrush = target ? TargetBrush : CardBrush,
            BorderThickness = new Thickness(target ? 3 : 1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(target ? 4 : 6),
            Margin = new Thickness(0, 0, 8, 8),
            Background = Brushes.Transparent,
            AllowDrop = true,
        };

        // Con el ratón: soltar aquí una app de un panel la libera.
        bool Accepts(DragEventArgs e) => e.Data.GetData(DragFormat) is DeckApp { PanelId: not null };
        card.DragEnter += (_, e) =>
        {
            e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
            if (Accepts(e))
            {
                card.Background = DropBrush;
            }
        };
        card.DragOver += (_, e) =>
        {
            e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        card.DragLeave += (_, _) => card.Background = Brushes.Transparent;
        card.Drop += async (_, e) =>
        {
            card.Background = Brushes.Transparent;
            _dragged = false;
            if (e.Data.GetData(DragFormat) is DeckApp { PanelId: not null, Handle: { } h })
            {
                await Run(false, new Release(h));
            }
        };
        return card;
    }

    /// <summary>Libera la app elegida (vuelve a su sitio original) y sale del modo mover.</summary>
    private async Task ReleaseChosen()
    {
        if (_moveApp is not { Handle: { } h, PanelId: not null })
        {
            return;
        }

        _moving = null;
        _moveApp = null;
        await Run(false, new Release(h));
    }

    /// <summary>Pone encima del contenido el velo amarillo de destino, con su texto; un toque hace <paramref name="onTap"/>.</summary>
    private static Grid WithTarget(UIElement content, string text, string name, Func<Task> onTap)
    {
        // Un botón de verdad (con su plantilla: el velo amarillo), no un Border que espera el ratón: con el
        // toque de la tablet por Apollo, sólo los botones respondían.
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetValue(Border.BackgroundProperty, TargetWash);
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        face.AppendChild(presenter);
        var overlay = new Button
        {
            Template = new ControlTemplate(typeof(Button)) { VisualTree = face },
            Focusable = false,
            Cursor = Cursors.Hand,
            // El texto en una etiqueta oscura: se lee aunque quede encima de las tarjetas.
            Content = new Border
            {
                Background = Background0,
                BorderBrush = TargetBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 6, 14, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, Foreground = TextBrush, FontSize = 16, FontWeight = FontWeights.SemiBold },
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(overlay, name);
        overlay.Click += async (_, _) => await onTap();
        var layered = new Grid();
        layered.Children.Add(content);
        layered.Children.Add(overlay);
        return layered;
    }

    /// <summary>Las apps de un panel o las libres: tantas como quepan; si no, la última celda es «+N más».</summary>
    private FitGrid AppGrid(IReadOnlyList<DeckApp> apps)
    {
        var grid = new FitGrid { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var app in apps)
        {
            grid.Children.Add(AppTile(app));
        }

        var more = new Button
        {
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = TextBrush,
            Background = CardBrush,
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        System.Windows.Automation.AutomationProperties.SetName(more, Strings.Deck_MoreName);
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more };
            foreach (var hidden in apps.Skip(grid.HiddenFrom))
            {
                var item = new MenuItem { Header = hidden.Name, IsEnabled = (_moving is null && !_closing) || hidden.Handle is not null };
                item.Click += async (_, _) => await TapApp(hidden);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        grid.More = more;
        grid.OverflowChanged = hidden => more.Content = Core.Text.F(Strings.Deck_More, hidden);
        grid.Children.Add(more);
        return grid;
    }

    /// <summary>
    /// Una app: tocarla la lleva al foco (si está cerrada, la abre en su panel; si es libre, la adopta y la
    /// lleva al foco). Clic derecho o pulsación larga: su menú.
    /// </summary>
    private Button AppTile(DeckApp app)
    {
        // El icono crece con la celda (hasta 32 px); el nombre, siempre del mismo tamaño y recortado.
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new Image
        {
            Source = app.App is { } catalog ? AppIcons.Get(catalog) : null,
            MaxWidth = 32,
            MaxHeight = 32,
            MinHeight = 14,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Opacity = Away(app) ? 0.45 : 1,
            Margin = new Thickness(0, 1, 0, 2),
        });
        var name = new TextBlock
        {
            Text = app.Handle is { } shown && shown == _closingNow ? Strings.Deck_Closing : app.Name + app.State switch
            {
                ListItemState.InFocus => " ↑",
                ListItemState.Parked => " ⏸",
                ListItemState.Detached => " ↗",
                ListItemState.OtherDesktop => " ⧉",
                _ => "",
            },
            Foreground = Away(app) ? DimBrush : TextBrush,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        Grid.SetRow(name, 1);
        content.Children.Add(name);

        // Anclada: un 📌 pequeño en la esquina (en el modo anclar ya lo dice el borde).
        if (IsPinned(app) && CanPin(app) && !_pinning)
        {
            content.Children.Add(new TextBlock
            {
                Text = "📌",
                FontSize = 10,
                Foreground = app.State == ListItemState.InFocus ? TextBrush : PinBrush, // sobre el azul del foco, blanco
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -2, -2, 0),
            });
        }

        var tile = new Button
        {
            // El tamaño lo pone la rejilla.
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(4),
            Focusable = false,
            BorderThickness = new Thickness(0),
            Background = app.State == ListItemState.InFocus ? ActiveBrush : CardBrush,
            Cursor = Cursors.Hand,
            ToolTip = app.State == ListItemState.OtherDesktop ? Core.Text.F(Strings.Deck_OtherDesktopTip, app.Name)
                : string.IsNullOrEmpty(app.Title) ? app.Name : $"{app.Name} · {app.Title}",
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"Deck: {app.Name} ({app.State})");
        // Modo mover (borde = se puede tocar; amarillo = mover): primero todas las que se pueden mover; ya
        // elegida, sólo ella conserva el borde hasta que se mueve o se cancela.
        if (_pinning)
        {
            // Borde = se puede tocar; índigo = anclar. Sólido: anclada; tenue: se puede anclar.
            if (CanPin(app))
            {
                tile.BorderBrush = IsPinned(app) ? PinBrush : PinFaintBrush;
                tile.BorderThickness = new Thickness(IsPinned(app) ? 3 : 2);
            }
            else
            {
                tile.Opacity = 0.4;
            }
        }
        else if (_moveApp is { } selected && selected.Handle == app.Handle && app.Handle is not null)
        {
            tile.BorderBrush = TargetBrush;
            tile.BorderThickness = new Thickness(3);
        }
        else if (_moving == MoveStep.PickApp && app.Handle is not null)
        {
            tile.BorderBrush = TargetBrush;
            tile.BorderThickness = new Thickness(2);
        }
        else if ((_moving is not null || _closing) && app.Handle is null)
        {
            tile.Opacity = 0.4; // una app cerrada no se mueve ni se cierra
        }
        else if (_closing)
        {
            tile.BorderBrush = WarnBrush;
            tile.BorderThickness = new Thickness(2);
        }
        else if (!InMode && app.Handle is { } shownHandle && _state is { } current && ForegroundMark(current) == shownHandle)
        {
            // Al frente (recibe el teclado) sin ser la del foco: un borde fino blanco, fuera de los colores
            // de los modos.
            tile.BorderBrush = TextBrush;
            tile.BorderThickness = new Thickness(2);
            tile.ToolTip = Core.Text.F(Strings.Deck_InFrontTip, app.Name);
        }

        tile.Click += async (_, _) =>
        {
            if (_dragged)
            {
                _dragged = false; // fue un arrastre, no un toque
                return;
            }

            await TapApp(app);
        };
        StartDragFrom(tile, app);
        tile.ContextMenu = MenuFor(app);
        return tile;
    }

    private static readonly Brush DropBrush = Frozen(Color.FromRgb(0x1F, 0x3A, 0x5F));

    private enum MoveStep
    {
        PickApp,
        PickPanel,
    }

    /// <summary>Mover con toques: <c>null</c> si no se está moviendo; si no, en qué paso va.</summary>
    private MoveStep? _moving;

    /// <summary>La app elegida para mover.</summary>
    private DeckApp? _moveApp;

    /// <summary>Mueve la app elegida a un panel (como al soltarla arrastrada) y sale del modo mover.</summary>
    private async Task MoveChosenTo(DeckPanel panel)
    {
        if (_moveApp is not { Handle: { } h } app)
        {
            return;
        }

        _moving = null;
        _moveApp = null;
        await Run(false, app.PanelId is null ? new Adopt(h, app.Name, panel.Id) : new SendTo(h, panel.Id));
    }
    private const string DragFormat = "FocusWShell.DeckApp";
    private Point? _dragStart;
    private bool _dragged;

    /// <summary>
    /// Arrastrar una app (ADR 0013): basta con moverla unos píxeles con el ratón o el dedo; si no, es un
    /// toque normal.
    /// </summary>
    private void StartDragFrom(Button tile, DeckApp app)
    {
        if (app.Handle is null)
        {
            return; // una app cerrada no se arrastra: se abre en su panel
        }

        tile.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        tile.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
        tile.PreviewMouseMove += (_, e) =>
        {
            if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var delta = e.GetPosition(this) - start;
            if (Math.Abs(delta.X) < 12 && Math.Abs(delta.Y) < 12)
            {
                return;
            }

            _dragStart = null;
            _dragged = true;
            DragDrop.DoDragDrop(tile, new DataObject(DragFormat, app), DragDropEffects.Move);
        };
    }

    /// <summary>Un panel como zona donde soltar: se ilumina al pasar por encima.</summary>
    private Border DropZone(UIElement content, DeckPanel panel)
    {
        var zone = new Border
        {
            Child = content,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 2, 0, 10),
            AllowDrop = true,
        };
        bool Accepts(DragEventArgs e) => e.Data.GetData(DragFormat) is DeckApp app && app.PanelId != panel.Id;
        zone.DragEnter += (_, e) =>
        {
            e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
            if (Accepts(e))
            {
                zone.Background = DropBrush;
            }
        };
        zone.DragOver += (_, e) =>
        {
            e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        zone.DragLeave += (_, _) => zone.Background = Brushes.Transparent;
        zone.Drop += async (_, e) =>
        {
            zone.Background = Brushes.Transparent;
            _dragged = false;
            if (e.Data.GetData(DragFormat) is DeckApp app && app.PanelId != panel.Id && app.Handle is { } h)
            {
                await Run(false, app.PanelId is null ? new Adopt(h, app.Name, panel.Id) : new SendTo(h, panel.Id));
            }
        };
        return zone;
    }

    /// <summary>
    /// Cabecera de un panel con su control de navegación: ◀ ▶ recorren sus pestañas (en pestañas o
    /// pantalla completa) y el botón de la presentación pasa a la siguiente.
    /// </summary>
    private DockPanel PanelHeader(DeckPanel panel)
    {
        var header = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = true };
        var tabs = panel.Apps.Where(a => a.State == ListItemState.InPanel && a.Handle is not null).ToList();
        bool cycles = panel.Template is PanelTemplate.Stack or PanelTemplate.Fill && tabs.Count > 1;
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        if (cycles)
        {
            // En pestañas, la visible es la primera: ▶ pone la segunda; ◀, la última.
            controls.Children.Add(NavButton("◀", Core.Text.F(Strings.Deck_PrevTab, panel.Name), () => Run(false, new MakeActiveInPanel(tabs[^1].Handle!.Value))));
            controls.Children.Add(NavButton("▶", Core.Text.F(Strings.Deck_NextTab, panel.Name), () => Run(false, new MakeActiveInPanel(tabs[1].Handle!.Value))));
        }

        // La presentación, como botones pegados: se ven todas, la activa en azul, y un toque basta para
        // cualquiera (antes, un botón mostraba la actual y al tocarlo pasaba a la siguiente).
        var order = panel.Display is null
            ? new[] { PanelTemplate.Tiles, PanelTemplate.Stack, PanelTemplate.List }
            : new[] { PanelTemplate.Tiles, PanelTemplate.Stack, PanelTemplate.List, PanelTemplate.Fill };
        var segments = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(cycles ? 10 : 0, 0, 0, 0) };
        foreach (var t in order)
        {
            bool on = t == panel.Template;
            var segment = NavButton(TemplateIcon(t), Core.Text.F(Strings.Deck_PresentationName, panel.Name, TemplateName(t)),
                () => on ? Task.CompletedTask : Run(false, new SetPresentation(panel.Id, t)));
            segment.Margin = new Thickness(2, 0, 0, 0);
            segment.FontSize = 20;
            segment.ToolTip = TemplateName(t);
            if (on)
            {
                segment.Background = ActiveBrush;
            }

            segments.Children.Add(segment);
        }

        controls.Children.Add(segments);
        DockPanel.SetDock(controls, Dock.Right);
        header.Children.Add(controls);
        var title = Label($"{panel.Name.ToUpperInvariant()} · {panel.Where}");
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        return header;
    }

    private static string TemplateName(PanelTemplate template) => template switch
    {
        PanelTemplate.Stack => Strings.Tpl_Stack,
        PanelTemplate.Fill => Strings.Tpl_Fill,
        PanelTemplate.List => Strings.Tpl_List,
        _ => Strings.Tpl_Tiles,
    };

    private static string TemplateIcon(PanelTemplate template) => template switch
    {
        PanelTemplate.Stack => "⧉",
        PanelTemplate.Fill => "⛶",
        PanelTemplate.List => "☰",
        _ => "▦",
    };

    /// <summary>Botón de la cabecera de un panel: del tamaño del dedo, como el resto del deck.</summary>
    private static Button NavButton(string text, string name, Func<Task> click)
    {
        var b = new Button
        {
            Content = text,
            Width = Touch,
            Height = Touch,
            Margin = new Thickness(4, 0, 0, 0),
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = TextBrush,
            Background = CardBrush,
            Cursor = Cursors.Hand,
            FontSize = 16,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: {name}");
        b.Click += async (_, _) => await click();
        return b;
    }

    /// <summary>Tocar una app: en modo mover, elegirla; si no, llevarla al foco (o abrirla).</summary>
    private async Task TapApp(DeckApp app)
    {
        if (_closing)
        {
            if (app.Handle is { } h)
            {
                // Como «Mover»: hecho lo pedido, el modo se apaga. La tarjeta dice «Cerrando…» al momento
                // y desaparece cuando la app se cierra (el deck se pone al día solo).
                _closing = false;
                _closingNow = h;
                Render();
                await _actions.CloseApp(h);

                // Si sigue abierta (preguntó algo y se canceló), la tarjeta vuelve a ser normal.
                await Task.Delay(5000);
                if (_closingNow == h)
                {
                    _closingNow = null;
                    Render();
                }
            }

            return;
        }

        if (_pinning)
        {
            if (CanPin(app))
            {
                _actions.SetPinned(app.AppKey, !IsPinned(app));
            }

            return;
        }

        if (_moving is not null)
        {
            if (app.Handle is not null)
            {
                // Tocar otra vez la elegida la suelta: se vuelve a elegir qué app mover.
                bool again = _moveApp?.Handle == app.Handle;
                _moveApp = again ? null : app;
                _moving = again ? MoveStep.PickApp : MoveStep.PickPanel;
                Render();
            }

            return;
        }

        await Choose(app);
    }

    private async Task Choose(DeckApp app)
    {
        // Cerrada, se abre en su panel; en otro escritorio, abrirla activa la que hay y Windows lleva allí.
        if (Away(app))
        {
            if (!Docked)
            {
                _actions.Hide();
            }

            await _actions.OpenApp(app.AppKey, app.PanelId!);
            return;
        }

        if (app.Handle is not { } h)
        {
            return;
        }

        // Ya está en el foco, pero puede tener una libre flotando encima (p. ej. una llamada): se trae al
        // frente, como con un clic del ratón, y la libre queda detrás, libre y en su sitio.
        if (app.State == ListItemState.InFocus)
        {
            if (!Docked)
            {
                _actions.Hide();
            }

            await _actions.BringToFront(h);
            return;
        }

        // Una libre sólo se trae al frente y sigue libre: hay ventanas que conviene dejar flotando (las de
        // una llamada de Zoom, un diálogo). Para ponerla en un panel o en el foco está «Mover».
        if (app.PanelId is null)
        {
            if (!Docked)
            {
                _actions.Hide();
            }

            await _actions.BringToFront(h);
            return;
        }

        await Run(true, new Promote(h));
    }

    private ContextMenu MenuFor(DeckApp app)
    {
        var menu = new ContextMenu();
        void Item(ItemsControl parent, string header, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            parent.Items.Add(item);
        }

        void PinItem()
        {
            if (CanPin(app))
            {
                Item(menu, IsPinned(app) ? Strings.DeckMenu_Unpin : Strings.DeckMenu_Pin, () =>
                {
                    _actions.SetPinned(app.AppKey, !IsPinned(app));
                    return Task.CompletedTask;
                });
            }
        }

        if (app.Handle is not { } h)
        {
            Item(menu, app.State == ListItemState.OtherDesktop ? Strings.DeckMenu_GoToDesktop : Strings.DeckMenu_OpenInPanel, () => Choose(app));
            PinItem();
            return menu;
        }

        // Un navegador: las páginas de todos.
        if (_state?.Browsers.Contains(h) == true && _actions.ReadPages is not null)
        {
            Item(menu, Strings.DeckMenu_Pages, OpenPages);
        }

        PinItem();

        bool free = app.PanelId is null;
        Item(menu, Strings.DeckMenu_ToFocus, () => Choose(app), app.State != ListItemState.InFocus);
        var fullscreen = new MenuItem { Header = Strings.DeckMenu_FullscreenOn };
        foreach (var (display, name) in _state!.Displays)
        {
            Item(fullscreen, name, () => Run(true, free ? [new Adopt(h, app.Name, DefaultPanel()), new EnterFullscreen(h, display)] : [new EnterFullscreen(h, display)]));
        }

        menu.Items.Add(fullscreen);
        if (!free)
        {
            Item(menu, app.State == ListItemState.Parked ? Strings.DeckMenu_Unpark : Strings.DeckMenu_Park,
                () => Run(false, app.State == ListItemState.Parked ? new Restore(h) : new Park(h)));
        }

        var move = new MenuItem { Header = free ? Strings.DeckMenu_PutIn : Strings.DeckMenu_MoveTo };
        foreach (var panel in _state.Panels.Where(p => p.Id != app.PanelId))
        {
            Item(move, panel.Name, () => Run(false, free ? new Adopt(h, app.Name, panel.Id) : new SendTo(h, panel.Id)));
        }

        menu.Items.Add(move);
        if (!free)
        {
            menu.Items.Add(new Separator());
            Item(menu, Strings.DeckMenu_Release, () => Run(false, new Release(h)));
        }

        return menu;
    }

    private string DefaultPanel() =>
        _state!.Panels.FirstOrDefault(p => p.Id == Workspace.WorkPanel)?.Id ?? _state.Panels[0].Id;

    /// <summary>
    /// "Mandar el deck a…": la esquina del principal (se oculta solo) o un monitor, en una mitad o entero;
    /// entero sólo si ese monitor no tiene paneles.
    /// </summary>
    private ContextMenu SendMenu(DeckState state)
    {
        var menu = new ContextMenu();
        var current = _actions.CurrentDock();
        void Item(string header, DeckDock? dock, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, IsCheckable = true, IsChecked = Equals(current, dock) };
            item.Click += (_, _) => _actions.Dock(dock);
            menu.Items.Add(item);
        }

        Item(Strings.DeckSend_Corner, null);
        foreach (var (display, name) in state.Displays.Where(d => d.Display is not null))
        {
            bool hasPanels = state.Panels.Any(p => string.Equals(p.Display, display, StringComparison.OrdinalIgnoreCase));
            menu.Items.Add(new Separator());
            Item(Core.Text.F(Strings.DeckSend_LeftHalf, name), new DeckDock(display!, DeckZone.LeftHalf));
            Item(Core.Text.F(Strings.DeckSend_RightHalf, name), new DeckDock(display!, DeckZone.RightHalf));
            Item(Core.Text.F(hasPanels ? Strings.DeckSend_WholeHasPanels : Strings.DeckSend_Whole, name), new DeckDock(display!, DeckZone.Whole), !hasPanels);
        }

        return menu;
    }

    /// <summary>Fijado en un monitor, el deck no se oculta al elegir: vive ahí.</summary>
    private bool Docked => _actions.CurrentDock() is not null;

    /// <summary>Ejecuta comandos; si <paramref name="hide"/> (se eligió una app), el deck de la esquina se oculta.</summary>
    private async Task Run(bool hide, params Command[] commands)
    {
        if (hide && !Docked)
        {
            _actions.Hide();
        }

        await _actions.Run(commands);
    }

    private static Button WideButton(string text, bool enabled, Action click) => WideButton(text, enabled, () =>
    {
        click();
        return Task.CompletedTask;
    });

    private static Button WideButton(string text, bool enabled, Func<Task> click)
    {
        var b = new Button
        {
            Content = text,
            MinWidth = 150,
            Height = Touch,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(12, 0, 12, 0),
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = enabled ? TextBrush : DimBrush,
            Background = CardBrush,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: {text.TrimStart('⇄', '↓', '◧', '▣', '◨', '☀', '☾', '⟲', ' ')}");
        b.Click += async (_, _) =>
        {
            if (enabled)
            {
                await click();
            }
        };
        return b;
    }

    private static Button SmallButton(string text, string name, Action click)
    {
        var b = new Button
        {
            Content = text,
            Width = Touch,
            Height = Touch,
            Focusable = false,
            BorderThickness = new Thickness(0),
            Foreground = TextBrush,
            Background = CardBrush,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Deck: {name}");
        b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Foreground = DimBrush, FontSize = 11, Margin = new Thickness(2, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
