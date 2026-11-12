using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;
using FocusWShell.Win32;

namespace FocusWShell.App;

/// <summary>Una app en el deck: abierta (con su ventana) o cerrada (del catálogo).</summary>
/// <param name="PanelId">Su panel; <c>null</c> si es una ventana libre.</param>
/// <param name="Handle">Su ventana, o <c>null</c> si está cerrada o en otro escritorio (no se puede mover aquí).</param>
public sealed record DeckApp(string Name, string AppKey, nint? Handle, ListItemState State, string Title, string? PanelId, CatalogApp? App);

/// <summary>Un panel en el deck, con sus apps.</summary>
/// <param name="Where">Dónde está, para la UI ("principal, izquierda", "HP 24fw, derecha").</param>
public sealed record DeckPanel(string Id, string Name, string Where, IReadOnlyList<DeckApp> Apps)
{
    /// <summary>Monitor del panel (<c>null</c>: el principal).</summary>
    public string? Display { get; init; }

    /// <summary>Su presentación.</summary>
    public PanelTemplate Template { get; init; }

    /// <summary>Su lado (columna) y su puesto en ella.</summary>
    public PanelSide Side { get; init; }

    public PanelSlot Slot { get; init; }

    /// <summary>En el principal, con su columna apagada (ADR 0012, enmienda): sus apps están minimizadas.</summary>
    public bool Off { get; init; }
}

/// <summary>
/// "API de deck" (ADR 0013): el estado que muestra el deck, calculado en un solo sitio para que lo use la
/// ventana del deck y, si llega, una versión web. Los comandos son los de siempre (<see cref="ShellController.Execute(Command[])"/>).
/// </summary>
/// <param name="Focus">Lo que hay en el foco (o en pantalla completa), si hay algo.</param>
/// <param name="Free">Ventanas que FocusWShell no gestiona.</param>
public sealed record DeckState(
    DeckApp? Focus,
    bool HasPrevious,
    FocusPosition View,
    IReadOnlyList<DeckPanel> Panels,
    IReadOnlyList<DeckApp> Free,
    IReadOnlyList<string> Scenes,
    IReadOnlyList<(string? Display, string Name)> Displays)
{
    /// <summary>Proporciones de la disposición actual.</summary>
    public ViewWeights Weights { get; init; } = ViewProportions.Default(FocusPosition.Center);

    /// <summary>Si el principal es ultrawide: ofrece traer una columna al otro lado (ADR 0012, enmienda).</summary>
    public bool Ultrawide { get; init; }

    /// <summary>La ventana al frente (la que recibe el teclado), sea o no de FocusWShell.</summary>
    public nint? Foreground { get; init; }

    /// <summary>Las ventanas de Chrome y Edge (aquí o en otro escritorio): con alguna, el deck ofrece «Páginas».</summary>
    public IReadOnlySet<nint> Browsers { get; init; } = new HashSet<nint>();
}

/// <summary>Una ventana de navegador con sus pestañas, para la lista de páginas del deck (spike S12).</summary>
/// <param name="Browser">Nombre del navegador («Microsoft Edge»).</param>
/// <param name="Where">Dónde está, para la UI: en el foco, su panel, libre u otro escritorio.</param>
/// <param name="Managed">Si está en un panel (tocar una página la lleva al foco) o es libre (sólo al frente).</param>
public sealed record PageWindow(nint Handle, string Browser, string Where, bool InFocus, bool Managed, bool OtherDesktop, CatalogApp? App, IReadOnlyList<BrowserTab> Tabs);
