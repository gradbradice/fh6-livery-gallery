using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Services;

namespace LiveryGallery.Controls;

/// <summary>
/// Days as a calendar of weeks (a column per week, Monday on top): the more downloads, the stronger the accent. One
/// hue from faint to full (a sequential scale); an empty day is the track colour. Hovering a day tells its date and count.
/// With <see cref="WeekdayValues"/>, the days of the week stand right of the calendar as bars, each in line with its
/// row (Monday on top), so a row of the calendar and its total read together
/// </summary>
internal sealed class CalendarHeatmap : Control
{
    public static readonly StyledProperty<IReadOnlyList<DayCount>?> DaysProperty =
        AvaloniaProperty.Register<CalendarHeatmap, IReadOnlyList<DayCount>?>(nameof(Days));

    /// <summary>How a day's tooltip reads: {0} the date, {1} the count</summary>
    public static readonly StyledProperty<string> TooltipFormatProperty =
        AvaloniaProperty.Register<CalendarHeatmap, string>(nameof(TooltipFormat), "{0}: {1}");

    /// <summary>Seven counts, Monday first; null or empty, no bars</summary>
    public static readonly StyledProperty<IReadOnlyList<int>?> WeekdayValuesProperty =
        AvaloniaProperty.Register<CalendarHeatmap, IReadOnlyList<int>?>(nameof(WeekdayValues));

    public static readonly StyledProperty<IReadOnlyList<string>?> WeekdayTooltipsProperty =
        AvaloniaProperty.Register<CalendarHeatmap, IReadOnlyList<string>?>(nameof(WeekdayTooltips));

    /// <summary>Above the bars, in the row of the month names</summary>
    public static readonly StyledProperty<string?> WeekdayTitleProperty =
        AvaloniaProperty.Register<CalendarHeatmap, string?>(nameof(WeekdayTitle));

    // A week's column takes the width it is given (the card stretches with the window), within these bounds; the gap
    // between days is a share of it
    private const double MinPitch = 9;
    private const double MaxPitch = 30;
    private const double GapShare = 0.16;

    // The sizes at the base step of a day (BasePitch). A wider card scales them all (Scale), so on a large window the
    // labels and the bars of the days of the week grow with the days instead of staying small beside them
    private const double BasePitch = 11.5;
    private const double BaseLeft = 26;
    private const double BaseTop = 16;
    private const double BaseFont = 9.5;
    // The labels stop growing here: larger text would crowd the days
    private const double MaxFontScale = 1.45;

    // The bars of the days of the week: a gap with a thin line, the day's name, the bar, its count
    private const double BaseSideGap = 24;
    private const double BaseSideLabel = 30;
    private const double BaseSideBar = 84;
    private const double BaseSideCount = 40;
    private const double BaseSideWidth = BaseSideGap + BaseSideLabel + BaseSideBar + BaseSideCount;

    // A day of the calendar (column, row), or a bar (WeekdayColumn, row)
    private const int WeekdayColumn = -2;
    private (int Column, int Row) _hovered = (-1, -1);

    static CalendarHeatmap()
    {
        AffectsRender<CalendarHeatmap>(DaysProperty, WeekdayValuesProperty, WeekdayTitleProperty);
        // More weeks or the bars: other days' size and height
        AffectsMeasure<CalendarHeatmap>(DaysProperty, WeekdayValuesProperty);
    }

    public CalendarHeatmap()
    {
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 0);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<DayCount>? Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public string TooltipFormat
    {
        get => GetValue(TooltipFormatProperty);
        set => SetValue(TooltipFormatProperty, value);
    }

    public IReadOnlyList<int>? WeekdayValues
    {
        get => GetValue(WeekdayValuesProperty);
        set => SetValue(WeekdayValuesProperty, value);
    }

    public IReadOnlyList<string>? WeekdayTooltips
    {
        get => GetValue(WeekdayTooltipsProperty);
        set => SetValue(WeekdayTooltipsProperty, value);
    }

    public string? WeekdayTitle
    {
        get => GetValue(WeekdayTitleProperty);
        set => SetValue(WeekdayTitleProperty, value);
    }

    private static int RowOf(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    private bool HasWeekdays => WeekdayValues is { Count: 7 };

    private double BaseSide => HasWeekdays ? BaseSideWidth : 0;

    /// <summary>
    /// The scale of every size and the step of a day for a width. Days, labels and bars grow together while the step
    /// stays under <see cref="MaxPitch"/>; a card narrower than the base sizes keeps them and shrinks the days alone
    /// </summary>
    private (double K, double Pitch) Scale(double width)
    {
        int weeks = Weeks();
        if (!double.IsFinite(width) || weeks == 0) return (1, 13);
        // The texts (the day labels on the left, the names and counts of the bars) grow up to MaxFontScale, the days, the
        // gap and the bars without a bound: the width is linear in k on each side of that point
        double texts = BaseLeft + (HasWeekdays ? BaseSideLabel + BaseSideCount : 0);
        double rest = weeks * BasePitch + (HasWeekdays ? BaseSideGap + BaseSideBar : 0);
        double k = width / (texts + rest);
        if (k > MaxFontScale) k = (width - texts * MaxFontScale) / rest;
        if (k >= 1)
        {
            double pitch = Math.Min(BasePitch * k, MaxPitch);
            return (pitch / BasePitch, pitch);
        }
        return (1, Math.Max(MinPitch, (width - BaseLeft - BaseSide) / weeks));
    }

    private double K => Scale(Bounds.Width).K;
    private double Left => BaseLeft * Math.Min(K, MaxFontScale);
    private double Top => BaseTop * Math.Min(K, MaxFontScale);
    private double FontSize => BaseFont * Math.Min(K, MaxFontScale);
    private double SideGap => BaseSideGap * K;
    private double SideLabel => BaseSideLabel * Math.Min(K, MaxFontScale);
    private double SideBar => BaseSideBar * K;
    private double SideWidth => SideGap + SideLabel + SideBar + BaseSideCount * Math.Min(K, MaxFontScale);

    protected override Size MeasureOverride(Size availableSize)
    {
        int weeks = Weeks();
        if (weeks == 0) return default;
        var (k, pitch) = Scale(availableSize.Width);
        // All the width offered, so the days spread over the card; the height follows the days' size
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(availableSize.Width, BaseLeft + weeks * MinPitch + BaseSide)
            : BaseLeft + weeks * pitch + BaseSide;
        return new Size(width, BaseTop * Math.Min(k, MaxFontScale) + 7 * pitch);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // A new width changes the days' size, and with it the height
        if (e.WidthChanged) InvalidateMeasure();
    }

    /// <summary>One day's step: its square and the gap after it</summary>
    private double Pitch => Scale(Bounds.Width).Pitch;

    private double Cell => Pitch * (1 - GapShare);
    private int Weeks() => Days is { Count: > 0 } days ? (days.Count + RowOf(days[0].Day) + 6) / 7 : 0;

    /// <summary>Where the bars begin: their line stands in the middle of the gap after the last week</summary>
    private double SideX => Left + Weeks() * Pitch + SideGap;

    private DayCount? At(int column, int row)
    {
        if (Days is not { Count: > 0 } days) return null;
        int index = column * 7 + row - RowOf(days[0].Day);
        return index >= 0 && index < days.Count ? days[index] : null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        double pitch = Pitch;
        int row = (int)Math.Floor((p.Y - Top) / pitch);
        (int, int) hovered = (-1, -1);
        string? tip = null;
        if (row is >= 0 and < 7 && HasWeekdays && p.X >= SideX - SideGap / 2)
        {
            hovered = (WeekdayColumn, row);
            tip = WeekdayTooltips is { } tips && row < tips.Count ? tips[row] : null;
        }
        else
        {
            int column = (int)Math.Floor((p.X - Left) / pitch);
            if (row is >= 0 and < 7 && column >= 0 && column < Weeks() && At(column, row) is { } d)
            {
                hovered = (column, row);
                tip = string.Format(AppLocalisationService.Culture, TooltipFormat, d.Day.ToString("D", AppLocalisationService.Culture), d.Count);
            }
        }
        if (hovered == _hovered) return;
        _hovered = hovered;
        if (tip is not null)
        {
            ToolTip.SetTip(this, tip);
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetIsOpen(this, true);
        }
        else
        {
            ToolTip.SetIsOpen(this, false);
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = (-1, -1);
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Days is not { Count: > 0 } days) return;
        var accent = ChartBrushes.Get(this, "AccentBrush") as ISolidColorBrush;
        var empty = ChartBrushes.Get(this, "HoverBrush");
        var muted = ChartBrushes.Get(this, "TextTertiaryBrush");
        var ring = new Pen(ChartBrushes.Get(this, "TextPrimaryBrush"), 1.5);
        // Four steps by the busiest days, so one huge day does not wash out the rest
        var nonZero = days.Where(d => d.Count > 0).Select(d => d.Count).Order().ToList();
        int Step(int count)
        {
            if (count == 0 || nonZero.Count == 0) return 0;
            int rank = nonZero.BinarySearch(count);
            if (rank < 0) rank = ~rank;
            return 1 + Math.Min(3, rank * 4 / nonZero.Count);
        }

        var culture = AppLocalisationService.Culture;
        int weeks = Weeks();
        double pitch = Pitch, cell = Cell, corner = Math.Max(2, cell * 0.22);
        int lastMonth = -1;
        for (int column = 0; column < weeks; column++)
        {
            for (int row = 0; row < 7; row++)
            {
                if (At(column, row) is not { } day) continue;
                var rect = new Rect(Left + column * pitch, Top + row * pitch, cell, cell);
                int step = Step(day.Count);
                IBrush brush = step == 0 || accent is null ? empty : new SolidColorBrush(accent.Color, 0.25 + 0.25 * (step - 1));
                context.DrawRectangle(brush, (column, row) == _hovered ? ring : null, rect, corner, corner);
                if (row == 0 || (column == 0 && row == RowOf(day.Day)))
                {
                    if (day.Day.Day <= 7 && day.Day.Month != lastMonth)
                    {
                        lastMonth = day.Day.Month;
                        Text(context, day.Day.ToString("MMM", culture), muted, new Point(rect.X, 0));
                    }
                }
            }
        }
        // Monday, Wednesday, Friday
        var monday = new DateOnly(2024, 1, 1);
        foreach (int row in new[] { 0, 2, 4 })
        {
            var label = Formatted(monday.AddDays(row).ToString("ddd", culture), muted);
            context.DrawText(label, new Point(0, Top + row * pitch + (cell - label.Height) / 2));
        }

        if (HasWeekdays) RenderWeekdays(context, monday, accent, muted, pitch, cell);
    }

    /// <summary>The days of the week, each bar in the middle of its calendar row, the longest as wide as the bars' room</summary>
    private void RenderWeekdays(DrawingContext context, DateOnly monday, IBrush? accent, IBrush muted, double pitch, double cell)
    {
        var values = WeekdayValues!;
        var culture = AppLocalisationService.Culture;
        double x = SideX;
        var line = new Pen(ChartBrushes.Get(this, "BorderBrush2"), 1);
        double lineX = Math.Round(x - SideGap / 2) + 0.5;
        context.DrawLine(line, new Point(lineX, Top), new Point(lineX, Top + 7 * pitch - (pitch - cell)));
        if (!string.IsNullOrEmpty(WeekdayTitle)) Text(context, WeekdayTitle, muted, new Point(x, 0));

        double max = Math.Max(1, values.Max());
        double barHeight = Math.Max(4, cell * 0.72);
        var primary = ChartBrushes.Get(this, "TextSecondaryBrush");
        for (int row = 0; row < 7; row++)
        {
            double y = Top + row * pitch;
            if (_hovered == (WeekdayColumn, row))
                context.DrawRectangle(ChartBrushes.Get(this, "HoverBrush"), null, new Rect(x - 6, y - (pitch - cell) / 2, SideWidth - SideGap + 6, pitch), 4, 4);
            var name = Formatted(monday.AddDays(row).ToString("ddd", culture), muted);
            context.DrawText(name, new Point(x, y + (cell - name.Height) / 2));
            double width = values[row] == 0 ? 0 : Math.Max(2, values[row] / max * SideBar);
            if (width > 0 && accent is not null)
                context.DrawRectangle(accent, null, new Rect(x + SideLabel, y + (cell - barHeight) / 2, width, barHeight), 2, 2);
            var count = Formatted(values[row].ToString("N0", culture), primary);
            context.DrawText(count, new Point(x + SideLabel + width + 6 * Math.Min(K, MaxFontScale), y + (cell - count.Height) / 2));
        }
    }

    private FormattedText Formatted(string text, IBrush brush) =>
        new(text, AppLocalisationService.Culture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), FontSize, brush);

    private void Text(DrawingContext context, string text, IBrush brush, Point at) => context.DrawText(Formatted(text, brush), at);
}
