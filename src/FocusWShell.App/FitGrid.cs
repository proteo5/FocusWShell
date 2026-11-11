using System.Windows;
using System.Windows.Controls;

namespace FocusWShell.App;

/// <summary>
/// Rejilla sin scroll para el deck: reparte sus hijos en celdas iguales del mayor tamaño que quepa en el
/// espacio que tiene, entre un mínimo cómodo para el dedo y un máximo. Si ni con celdas mínimas caben,
/// la última celda muestra <see cref="More"/> («+N más») y los que no caben no se muestran.
/// </summary>
public sealed class FitGrid : Panel
{
    /// <summary>Ancho máximo de una celda.</summary>
    public double MaxCellWidth { get; set; } = 132;

    /// <summary>Ancho mínimo de una celda.</summary>
    public double MinCellWidth { get; set; } = 64;

    /// <summary>Alto mínimo de una celda (al menos un dedo).</summary>
    public double MinCellHeight { get; set; } = 48;

    /// <summary>Alto máximo de una celda; si es 0, el que dé la proporción.</summary>
    public double MaxCellHeight { get; set; }

    /// <summary>Ancho entre alto de una celda.</summary>
    public double Aspect { get; set; } = 1.35;

    public double Gap { get; set; } = 6;

    /// <summary>
    /// Elemento «+N más», hijo también de la rejilla. Se muestra en la última celda si no caben todos;
    /// <see cref="HiddenFrom"/> dice desde qué hijo quedaron fuera.
    /// </summary>
    public FrameworkElement? More { get; set; }

    /// <summary>Se llama con el número de ocultos al cambiar (para poner el texto de <see cref="More"/>).</summary>
    public Action<int>? OverflowChanged { get; set; }

    /// <summary>Índice (entre los hijos que no son <see cref="More"/>) del primero que no se muestra.</summary>
    public int HiddenFrom { get; private set; } = int.MaxValue;

    private (int Cols, double W, double H, int Visible) _layout;

    private List<UIElement> Items => InternalChildren.Cast<UIElement>().Where(c => c != More).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        double width = double.IsInfinity(availableSize.Width) ? (MaxCellWidth + Gap) * Math.Max(1, items.Count) : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? (MinCellHeight + Gap) * Math.Max(1, items.Count) : availableSize.Height;
        _layout = Plan(items.Count, width, height);

        int hidden = items.Count - _layout.Visible;
        int hiddenFrom = hidden > 0 ? _layout.Visible : int.MaxValue;
        if (hiddenFrom != HiddenFrom)
        {
            HiddenFrom = hiddenFrom;
            OverflowChanged?.Invoke(Math.Max(0, hidden));
        }

        var cell = new Size(_layout.W, _layout.H);
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Measure(i < _layout.Visible ? cell : default);
        }

        More?.Measure(hidden > 0 ? cell : default);
        int shown = _layout.Visible + (hidden > 0 ? 1 : 0);
        int rows = shown == 0 ? 0 : (int)Math.Ceiling(shown / (double)_layout.Cols);
        return new Size(
            Math.Min(width, (_layout.Cols * _layout.W) + ((_layout.Cols - 1) * Gap)),
            Math.Min(height, (rows * _layout.H) + (Math.Max(0, rows - 1) * Gap)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Items;
        var hiddenRect = new Rect(0, 0, 0, 0);
        int slot = 0;
        Rect Next()
        {
            int col = slot % _layout.Cols;
            int row = slot / _layout.Cols;
            slot++;
            return new Rect(col * (_layout.W + Gap), row * (_layout.H + Gap), _layout.W, _layout.H);
        }

        for (int i = 0; i < items.Count; i++)
        {
            items[i].Arrange(i < _layout.Visible ? Next() : hiddenRect);
        }

        More?.Arrange(items.Count > _layout.Visible ? Next() : hiddenRect);
        return finalSize;
    }

    /// <summary>Columnas, tamaño de celda y cuántos caben (dejando sitio a «+N más» si hace falta).</summary>
    private (int Cols, double W, double H, int Visible) Plan(int count, double width, double height)
    {
        if (count == 0)
        {
            return (1, MaxCellWidth, MinCellHeight, 0);
        }

        double CellHeight(double w)
        {
            double h = Math.Max(MinCellHeight, w / Aspect);
            return MaxCellHeight > 0 ? Math.Min(h, Math.Max(MinCellHeight, MaxCellHeight)) : h;
        }

        // La celda más ancha con la que caben todas.
        (int Cols, double W, double H)? best = null;
        for (int cols = 1; cols <= count; cols++)
        {
            double w = Math.Min(MaxCellWidth, (width - ((cols - 1) * Gap)) / cols);
            if (w < MinCellWidth)
            {
                break;
            }

            double h = CellHeight(w);
            int rows = (int)Math.Ceiling(count / (double)cols);
            // La celda más grande; a igualdad, más columnas (menos filas): se llena a lo ancho.
            if ((rows * h) + ((rows - 1) * Gap) <= height + 0.5 && (best is null || w > best.Value.W + 0.5 || (Math.Abs(w - best.Value.W) <= 0.5 && cols > best.Value.Cols)))
            {
                best = (cols, w, h);
            }
        }

        if (best is { } fit)
        {
            return (fit.Cols, fit.W, fit.H, count);
        }

        // No caben: celdas mínimas, tantas como entren; la última es «+N más».
        int c = Math.Max(1, (int)((width + Gap) / (MinCellWidth + Gap)));
        double cw = Math.Min(MaxCellWidth, (width - ((c - 1) * Gap)) / c);
        double ch = MinCellHeight;
        int r = Math.Max(1, (int)((height + Gap) / (ch + Gap)));
        int capacity = c * r;
        return (c, cw, ch, capacity >= count ? count : Math.Max(0, capacity - 1));
    }
}
