using FocusWShell.Core.Geometry;

namespace FocusWShell.Core.Layout;

/// <summary>Qué parte de su monitor ocupa el deck fijado (ADR 0013, fase 4).</summary>
public enum DeckZone
{
    /// <summary>La mitad izquierda; los paneles de ese monitor, en la derecha.</summary>
    LeftHalf,

    /// <summary>La mitad derecha; los paneles, en la izquierda.</summary>
    RightHalf,

    /// <summary>Todo el monitor: sólo en uno sin paneles.</summary>
    Whole,
}

/// <summary>
/// El deck fijado en un monitor (p. ej. la tablet): ocupa una zona fija y los paneles de ese monitor se
/// colocan en el resto, sin quedar tapados.
/// </summary>
/// <param name="Display">Clave estable del monitor.</param>
public sealed record DeckDock(string Display, DeckZone Zone)
{
    /// <summary>Divide el área de trabajo del monitor entre el deck y los paneles (<c>null</c> si no queda sitio).</summary>
    public static (PixelRect Deck, PixelRect? Panels) Split(PixelRect workArea, DeckZone zone)
    {
        int middle = workArea.Left + (workArea.Width / 2);
        return zone switch
        {
            DeckZone.LeftHalf => (workArea with { Right = middle }, workArea with { Left = middle }),
            DeckZone.RightHalf => (workArea with { Left = middle }, workArea with { Right = middle }),
            _ => (workArea, null),
        };
    }
}
