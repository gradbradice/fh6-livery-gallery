using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using LiveryGallery.Models;

namespace LiveryGallery.Views;

public sealed class GalleryRowPresenter : Panel
{
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<GalleryRowPresenter, IDataTemplate?>(nameof(ItemTemplate));

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    private int _visibleCount;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateCards();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemTemplateProperty) UpdateCards();
    }

    private void UpdateCards()
    {
        var items = (DataContext as GalleryRow)?.Items;
        int count = items?.Count ?? 0;

        for (int i = 0; i < count; i++)
        {
            var item = items![i];
            if (i < Children.Count)
            {
                var card = Children[i];
                if (!ReferenceEquals(card.DataContext, item)) card.DataContext = item;
                card.IsVisible = true;
            }
            else
            {
                var card = ItemTemplate?.Build(item);
                if (card is null) break;
                card.DataContext = item;
                Children.Add(card);
            }
        }

        for (int i = count; i < Children.Count; i++)
        {
            var card = Children[i];
            if (card.DataContext is not null) card.DataContext = null;
            card.IsVisible = false;
        }

        _visibleCount = Math.Min(count, Children.Count);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
        double width = 0, height = 0;
        for (int i = 0; i < _visibleCount; i++)
        {
            var card = Children[i];
            card.Measure(childConstraint);
            width += card.DesiredSize.Width;
            height = Math.Max(height, card.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < _visibleCount; i++)
        {
            var card = Children[i];
            double width = card.DesiredSize.Width;
            card.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}
