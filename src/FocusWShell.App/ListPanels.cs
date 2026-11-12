using FocusWShell.Core.Domain;
using FocusWShell.Core.Geometry;
using FocusWShell.Core.Layout;

namespace FocusWShell.App;

/// <summary>Estado de una app en la lista de su panel.</summary>
public enum ListItemState
{
    /// <summary>En el panel (detrás de la lista).</summary>
    InPanel,

    /// <summary>En el foco o en pantalla completa: volverá a este panel.</summary>
    InFocus,

    /// <summary>Estacionada.</summary>
    Parked,

    /// <summary>Fuera de layout.</summary>
    Detached,

    /// <summary>En otro escritorio virtual (ADR 0016): no está aquí; tocarla lleva a su escritorio.</summary>
    OtherDesktop,

    /// <summary>Cerrada: está en el catálogo y su último panel es éste. Tocarla la abre.</summary>
    Closed,
}

/// <summary>Una app en la lista de un panel.</summary>
/// <param name="Handle">Su ventana, o <c>null</c> si está cerrada.</param>
public sealed record ListItem(string Name, string AppKey, nint? Handle, ListItemState State, string Title, CatalogApp? App);

/// <summary>Lista tipo Explorador de un panel (ADR 0015): dónde va y qué muestra.</summary>
/// <param name="Area">Área del panel bajo su tira, en píxeles físicos.</param>
public sealed record ListPanelView(string PanelId, string Title, PixelRect Area, int Dpi, ListLayout Layout, IReadOnlyList<ListItem> Items)
{
    public string Key => PanelId;

    /// <summary>Para no redibujar si nada cambió.</summary>
    public string Signature => $"{Title}|{Layout}|{string.Join(";", Items.Select(i => $"{i.Handle}:{i.State}:{i.Name}:{i.Title}"))}";
}
