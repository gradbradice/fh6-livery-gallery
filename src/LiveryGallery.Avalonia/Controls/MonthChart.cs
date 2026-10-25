using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using LiveryGallery.Services;

namespace LiveryGallery.Controls;

/// <summary>One month of a <see cref="MonthChart"/></summary>
/// <param name="Month">The month</param>
/// <param name="Up">Stacked above the axis, in the order of <see cref="MonthChart.UpSeries"/></param>
/// <param name="Down">Stacked below the axis, in the order of <see cref="MonthChart.DownSeries"/></param>
/// <param name="Value">The line's value (line mode); null where it has none</param>
/// <param name="Estimated">Drawn fainter: the month is from before the journal</param>
/// <param name="Tooltip">What hovering the month says</param>
internal sealed record ChartColumn(DateOnly Month, double[] Up, double[] Down, double? Value, bool Estimated, string Tooltip);

/// <summary>The colours of the charts' series, validated for colour-blind readers as stacked pairs in both themes</summary>
internal static class ChartPalette
{
    /// <summary>A series colour: "accent" (the theme's), "faint" (the accent, faded: an estimate), "aqua", "orange" or "blue"</summary>
    public static IBrush Brush(Control control, string name)
    {
        bool dark = control.ActualThemeVariant == ThemeVariant.Dark;
        return name switch
        {
            "aqua" => Solid(dark ? "#199E70" : "#1BAF7A"),
            "orange" => Solid(dark ? "#D95926" : "#EB6834"),
            "blue" => Solid(dark ? "#3987E5" : "#2A78D6"),
            "faint" => Faint(ChartBrushes.Get(control, "AccentBrush")),
            _ => ChartBrushes.Get(control, "AccentBrush"),
        };
    }

    private static readonly Dictionary<string, IBrush> Cache = [];

    private static IBrush Faint(IBrush accent) =>
        accent is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.4).ToImmutable() : accent;

    private static IBrush Solid(string hex)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(hex, out var brush)) Cache[hex] = brush = new SolidColorBrush(Color.Parse(hex)).ToImmutable();
            return brush;
        }
    }
}

/// <summary>A legend's key: a short rounded stroke of a series colour (<see cref="ChartPalette.Brush"/>)</summary>
internal sealed class SeriesKey : Control
{
    public static readonly StyledProperty<string?> SeriesProperty =
        AvaloniaProperty.Register<SeriesKey, string?>(nameof(Series));

    static SeriesKey() => AffectsRender<SeriesKey>(SeriesProperty);

    public SeriesKey() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public string? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double radius = Bounds.Height / 2;
        context.DrawRectangle(ChartPalette.Brush(this, Series ?? "accent"), null, new Rect(Bounds.Size), radius, radius);
    }
}

/// <summary>
/// Months side by side: stacked columns above and below one axis (what came and what went), or a line (a total). Thin
/// marks with rounded outer ends, a 2px gap between stacked parts, a recessive grid, every month hoverable for its
/// numbers. One axis only: a different measure gets a chart of its own
/// </summary>
internal sealed class MonthChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<MonthChart, IReadOnlyList<ChartColumn>?>(nameof(Columns));

    public static readonly StyledProperty<IReadOnlyList<string>?> UpSeriesProperty =
        AvaloniaProperty.Register<MonthChart, IReadOnlyList<string>?>(nameof(UpSeries));

    public static readonly StyledProperty<IReadOnlyList<string>?> DownSeriesProperty =
        AvaloniaProperty.Register<MonthChart, IReadOnlyList<string>?>(nameof(DownSeries));

    public static readonly StyledProperty<bool> IsLineProperty =
        AvaloniaProperty.Register<MonthChart, bool>(nameof(IsLine));

    private const double AxisWidth = 40;
    private const double LabelHeight = 20;
    private const double TopPadding = 6;
    private int _hovered = -1;

    static MonthChart()
    {
        AffectsRender<MonthChart>(ColumnsProperty, UpSeriesProperty, DownSeriesProperty, IsLineProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MonthChart>(true);
    }

    public MonthChart()
    {
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 0);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<ChartColumn>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>The series stacked above the axis, from the axis up (<see cref="ChartPalette.Brush"/> names)</summary>
    public IReadOnlyList<string>? UpSeries
    {
        get => GetValue(UpSeriesProperty);
        set => SetValue(UpSeriesProperty, value);
    }

    /// <summary>The series stacked below the axis, from the axis down</summary>
    public IReadOnlyList<string>? DownSeries
    {
        get => GetValue(DownSeriesProperty);
        set => SetValue(DownSeriesProperty, value);
    }

    /// <summary>Draw <see cref="ChartColumn.Value"/> as a line instead of columns</summary>
    public bool IsLine
    {
        get => GetValue(IsLineProperty);
        set => SetValue(IsLineProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty) SetHovered(-1);
    }

    private Rect Plot => new(AxisWidth, TopPadding, Math.Max(0, Bounds.Width - AxisWidth - 4), Math.Max(0, Bounds.Height - LabelHeight - TopPadding));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var columns = Columns;
        var plot = Plot;
        if (columns is not { Count: > 0 } || plot.Width <= 0)
        {
            SetHovered(-1);
            return;
        }
        double x = e.GetPosition(this).X - plot.X;
        int index = x < 0 || x >= plot.Width ? -1 : (int)(x / (plot.Width / columns.Count));
        SetHovered(Math.Min(index, columns.Count - 1));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(-1);
    }

    private void SetHovered(int index)
    {
        if (index == _hovered) return;
        _hovered = index;
        var columns = Columns;
        if (index >= 0 && columns is not null && index < columns.Count)
        {
            ToolTip.SetTip(this, columns[index].Tooltip);
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetIsOpen(this, true);
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var columns = Columns;
        var plot = Plot;
        if (columns is not { Count: > 0 } || plot.Width < 10 || plot.Height < 10) return;

        var grid = new Pen(ChartBrushes.Get(this, "BorderBrush2"), 1);
        var axis = new Pen(ChartBrushes.Get(this, "TextTertiaryBrush"), 1);
        var muted = ChartBrushes.Get(this, "TextTertiaryBrush");
        double step = plot.Width / columns.Count;

        if (_hovered >= 0 && _hovered < columns.Count)
            context.DrawRectangle(ChartBrushes.Get(this, "HoverBrush"), null,
                new Rect(plot.X + _hovered * step, plot.Y, step, plot.Height), 4, 4);

        if (IsLine) RenderLine(context, columns, plot, step, grid, axis, muted);
        else RenderColumns(context, columns, plot, step, grid, axis, muted);
        RenderMonthLabels(context, columns, plot, step, muted);
    }

    private void RenderColumns(DrawingContext context, IReadOnlyList<ChartColumn> columns, Rect plot, double step, Pen grid, Pen axis, IBrush muted)
    {
        double maxUp = Math.Max(1, columns.Max(c => c.Up.Sum()));
        double maxDown = columns.Max(c => c.Down.Sum());
        double total = maxUp + maxDown;
        double zero = plot.Y + plot.Height * (maxUp / total);
        double scale = plot.Height / total;

        foreach (double value in NiceTicks(maxUp))
        {
            double y = zero - value * scale;
            context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(context, Format(value), muted, new Point(plot.X - 6, y), alignRight: true);
        }
        if (maxDown > 0)
        {
            double tick = NiceTicks(maxDown).LastOrDefault();
            if (tick > 0)
            {
                double y = zero + tick * scale;
                context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
                DrawText(context, "−" + Format(tick), muted, new Point(plot.X - 6, y), alignRight: true);
            }
        }

        double barWidth = Math.Clamp(step * 0.62, 2, 22);
        var up = UpSeries ?? [];
        var down = DownSeries ?? [];
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            double x = plot.X + i * step + (step - barWidth) / 2;
            using var _ = context.PushOpacity(column.Estimated ? 0.45 : 1);
            Stack(context, column.Up, up, x, barWidth, zero, -scale);
            Stack(context, column.Down, down, x, barWidth, zero, scale);
        }
        context.DrawLine(axis, new Point(plot.X, zero), new Point(plot.Right, zero));
    }

    /// <summary>Stacked parts away from the axis; the outermost one gets the rounded end</summary>
    private void Stack(DrawingContext context, double[] values, IReadOnlyList<string> series, double x, double width, double zero, double scale)
    {
        int last = Array.FindLastIndex(values, v => v > 0);
        double at = zero;
        for (int s = 0; s <= last; s++)
        {
            if (values[s] <= 0) continue;
            double length = Math.Max(2, Math.Abs(values[s] * scale));
            double from = at, to = at + Math.Sign(scale) * length;
            // A 2px gap of the surface between stacked parts
            double gap = s == 0 ? 0 : 2;
            double top = Math.Min(from, to) + (scale < 0 ? 0 : gap);
            double bottom = Math.Max(from, to) - (scale < 0 ? gap : 0);
            if (bottom - top < 1) { at = to; continue; }
            var brush = ChartPalette.Brush(this, s < series.Count ? series[s] : "accent");
            var rect = new Rect(x, top, width, bottom - top);
            if (s == last) context.DrawGeometry(brush, null, RoundedEnd(rect, roundTop: scale < 0));
            else context.DrawRectangle(brush, null, rect);
            at = to;
        }
    }

    private static StreamGeometry RoundedEnd(Rect rect, bool roundTop)
    {
        double r = Math.Min(4, Math.Min(rect.Width / 2, rect.Height));
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        if (roundTop)
        {
            g.BeginFigure(new Point(rect.Left, rect.Bottom), true);
            g.LineTo(new Point(rect.Left, rect.Top + r));
            g.ArcTo(new Point(rect.Left + r, rect.Top), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(rect.Right - r, rect.Top));
            g.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(rect.Right, rect.Bottom));
        }
        else
        {
            g.BeginFigure(new Point(rect.Left, rect.Top), true);
            g.LineTo(new Point(rect.Right, rect.Top));
            g.LineTo(new Point(rect.Right, rect.Bottom - r));
            g.ArcTo(new Point(rect.Right - r, rect.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(rect.Left + r, rect.Bottom));
            g.ArcTo(new Point(rect.Left, rect.Bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
        }
        g.EndFigure(true);
        return geometry;
    }

    private void RenderLine(DrawingContext context, IReadOnlyList<ChartColumn> columns, Rect plot, double step, Pen grid, Pen axis, IBrush muted)
    {
        var values = columns.Select(c => c.Value).ToList();
        if (!values.Any(v => v.HasValue)) return;
        double max = Math.Max(1, values.Max(v => v ?? 0));
        double scale = plot.Height / max;
        foreach (double value in NiceTicks(max))
        {
            double y = plot.Bottom - value * scale;
            context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(context, Format(value), muted, new Point(plot.X - 6, y), alignRight: true);
        }
        context.DrawLine(axis, new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));

        var brush = ChartPalette.Brush(this, "accent");
        var pen = new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Point? previous = null;
        for (int i = 0; i < columns.Count; i++)
        {
            if (values[i] is not { } value)
            {
                previous = null;
                continue;
            }
            var point = new Point(plot.X + i * step + step / 2, plot.Bottom - value * scale);
            if (previous is { } from) context.DrawLine(pen, from, point);
            else if (i + 1 >= columns.Count || values[i + 1] is null) context.DrawEllipse(brush, null, point, 2.5, 2.5);
            previous = point;
        }
        if (_hovered >= 0 && _hovered < columns.Count && values[_hovered] is { } hovered)
        {
            var point = new Point(plot.X + _hovered * step + step / 2, plot.Bottom - hovered * scale);
            context.DrawEllipse(brush, new Pen(ChartBrushes.Get(this, "CardBrush"), 2), point, 5, 5);
        }
    }

    private void RenderMonthLabels(DrawingContext context, IReadOnlyList<ChartColumn> columns, Rect plot, double step, IBrush muted)
    {
        // As many labels as fit, always on the same months: January (with its year) and every n-th month after it
        int every = step >= 34 ? 1 : step >= 17 ? 2 : step >= 11 ? 3 : step >= 5.5 ? 6 : 12;
        var culture = AppLocalisationService.Culture;
        for (int i = 0; i < columns.Count; i++)
        {
            var month = columns[i].Month;
            if ((month.Month - 1) % every != 0) continue;
            string text = month.Month == 1 || i == 0
                ? month.ToString("MMM yy", culture)
                : month.ToString("MMM", culture);
            DrawText(context, text, muted, new Point(plot.X + i * step + step / 2, plot.Bottom + 4), center: true, top: true);
        }
    }

    private static IEnumerable<double> NiceTicks(double max)
    {
        if (max <= 0) yield break;
        double raw = max / 4;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double nice = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(m => m >= raw);
        nice = Math.Max(1, Math.Round(nice));
        for (double tick = nice; tick <= max + 1e-9; tick += nice) yield return tick;
    }

    private static string Format(double value) => value.ToString("N0", AppLocalisationService.Culture);

    private void DrawText(DrawingContext context, string text, IBrush brush, Point at, bool alignRight = false, bool center = false, bool top = false)
    {
        var formatted = new FormattedText(text, AppLocalisationService.Culture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 10.5, brush);
        double x = alignRight ? at.X - formatted.Width : center ? at.X - formatted.Width / 2 : at.X;
        double y = top ? at.Y : at.Y - formatted.Height / 2;
        context.DrawText(formatted, new Point(x, y));
    }
}
