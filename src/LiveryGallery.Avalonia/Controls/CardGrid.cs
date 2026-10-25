using Avalonia;
using Avalonia.Controls;

namespace LiveryGallery.Controls;

/// <summary>
/// Cards in equal columns, row by row, each row as tall as its tallest card. Unlike a UniformGrid, a short row does not
/// take the height of the tallest row of the whole grid
/// </summary>
internal sealed class CardGrid : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<CardGrid, int>(nameof(Columns), 2);

    static CardGrid() => AffectsMeasure<CardGrid>(ColumnsProperty);

    /// <summary>Cards in a row; at least 1</summary>
    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = Math.Max(1, Columns);
        var cards = Children.Where(c => c.IsVisible).ToList();
        double width = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / columns;
        double height = 0, widest = 0;
        for (int start = 0; start < cards.Count; start += columns)
        {
            double row = 0;
            foreach (var card in cards.Skip(start).Take(columns))
            {
                card.Measure(new Size(width, double.PositiveInfinity));
                row = Math.Max(row, card.DesiredSize.Height);
                widest = Math.Max(widest, card.DesiredSize.Width);
            }
            height += row;
        }
        return new Size(double.IsInfinity(width) ? widest * columns : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = Math.Max(1, Columns);
        var cards = Children.Where(c => c.IsVisible).ToList();
        double width = finalSize.Width / columns, y = 0;
        for (int start = 0; start < cards.Count; start += columns)
        {
            var row = cards.Skip(start).Take(columns).ToList();
            double height = row.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * width, y, width, height));
            y += height;
        }
        return finalSize;
    }
}
