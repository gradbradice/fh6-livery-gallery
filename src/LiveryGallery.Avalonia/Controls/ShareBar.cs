using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LiveryGallery.Controls;

/// <summary>A thin horizontal bar: a share of the whole, drawn from the left on a faint track</summary>
internal sealed class ShareBar : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<ShareBar, double>(nameof(Fraction));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ShareBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ShareBar, IBrush?>(nameof(Track));

    static ShareBar() => AffectsRender<ShareBar>(FractionProperty, FillProperty, TrackProperty);

    /// <summary>From 0 to 1</summary>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <summary>The bar; the accent colour when not set</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Under the bar; the hover colour when not set</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        double radius = Math.Min(4, bounds.Height / 2);
        context.DrawRectangle(Track ?? ChartBrushes.Get(this, "HoverBrush"), null, bounds, radius, radius);
        double width = bounds.Width * Math.Clamp(Fraction, 0, 1);
        if (width <= 0) return;
        // A sliver stays visible: a share of one livery in thousands is still there
        width = Math.Max(width, Math.Min(bounds.Width, bounds.Height));
        context.DrawRectangle(Fill ?? ChartBrushes.Get(this, "AccentBrush"), null, new Rect(0, 0, width, bounds.Height), radius, radius);
    }
}

/// <summary>The theme's brushes for controls that draw themselves</summary>
internal static class ChartBrushes
{
    public static IBrush Get(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
