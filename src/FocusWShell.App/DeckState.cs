using FocusWShell.Core.Domain;
using FocusWShell.Core.Layout;

namespace FocusWShell.App;

/// <summary>Una app en el deck: abierta (con su ventana) o cerrada (del catálogo).</summary>
/// <param name="Handle">Su ventana, o <c>null</c> si está cerrada.</param>
/// <param name="PanelId">Su panel; <c>null</c> si es una ventana libre.</param>
public sealed record DeckApp(string Name, string AppKey, nint? Handle, ListItemState State, string Title, string? PanelId, CatalogApp? App);

/// <summary>Un panel en el deck, con sus apps.</summary>
/// <param name="Where">Dónde está, para la UI ("principal, izquierda", "HP 24fw, derecha").</param>
public sealed record DeckPanel(string Id, string Name, string Where, IReadOnlyList<DeckApp> Apps)
{
    /// <summary>Monitor del panel (<c>null</c>: el principal).</summary>
    public string? Display { get; init; }

    /// <summary>Su presentación.</summary>
    public PanelTemplate Template { get; init; }
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
    /// <summary>Proporciones de la vista actual (para los presets de ancho del foco).</summary>
    public ViewWeights Weights { get; init; } = ViewProportions.Default(FocusPosition.Center);

    /// <summary>La ventana al frente (la que recibe el teclado), sea o no de FocusWShell.</summary>
    public nint? Foreground { get; init; }
}
