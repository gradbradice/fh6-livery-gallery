using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace LiveryGallery.Views;

public sealed class VirtualGalleryPanel : Panel
{
    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<VirtualGalleryPanel, IDataTemplate?>(nameof(HeaderTemplate));

    public static readonly StyledProperty<IDataTemplate?> RowTemplateProperty =
        AvaloniaProperty.Register<VirtualGalleryPanel, IDataTemplate?>(nameof(RowTemplate));

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public IDataTemplate? RowTemplate
    {
        get => GetValue(RowTemplateProperty);
        set => SetValue(RowTemplateProperty, value);
    }

    public Func<object, IEnumerable<string>>? AnchorKeySelector { get; set; }

    private const double DefaultHeaderHeight = 56;
    private const double DefaultRowHeight = 340;
    private const double MinBuffer = 300;
    private const int MaxPooledPerKind = 12;

    // Once realized, the viewport may move this far inside the realized area before a new
    // measure pass is needed
    private const double RemeasureMargin = 120;
    private const int MaxAnchorCandidates = 8;

    private IReadOnlyList<object> _items = [];
    private bool[] _isHeader = [];
    private double[] _heights = [];      // NaN = not measured yet at the current width
    private double[] _tops = [0];        // _tops[i] = top of item i; _tops[n] = total height
    private bool _topsDirty = true;
    private double _coveredTop, _coveredBottom = -1; // area realized by the last measure pass

    private readonly Dictionary<object, double> _knownHeights = new(ReferenceEqualityComparer.Instance);
    private double _measureWidth = double.NaN;

    private double _headerHeightSum, _rowHeightSum;
    private int _headerHeightCount, _rowHeightCount;

    private readonly Dictionary<int, Control> _realized = [];
    private readonly Dictionary<object, Control> _reusable = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, bool> _controlIsHeader = [];
    private readonly Stack<Control> _headerPool = new();
    private readonly Stack<Control> _rowPool = new();

    private Rect _viewport;
    private double _pendingScrollDelta;
    private List<(string[] Keys, double RelativeTop)>? _pendingAnchor;
    private ScrollViewer? _scrollViewer;

    public VirtualGalleryPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    public void SetItems(IReadOnlyList<object> items, bool preservePosition)
    {
        _pendingAnchor = preservePosition ? CaptureAnchor() : null;
        foreach (var (index, control) in _realized)
        {
            if (!_reusable.TryAdd(_items[index], control)) Recycle(control);
        }
        _realized.Clear();

        _items = items;
        int n = items.Count;
        _isHeader = new bool[n];
        _heights = new double[n];

        var stillKnown = new Dictionary<object, double>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < n; i++)
        {
            var item = items[i];
            _isHeader[i] = HeaderTemplate?.Match(item) == true;
            if (_knownHeights.TryGetValue(item, out double height))
            {
                _heights[i] = height;
                stillKnown[item] = height;
            }
            else
            {
                _heights[i] = double.NaN;
            }
        }

        _knownHeights.Clear();
        foreach (var (item, height) in stillKnown) _knownHeights[item] = height;

        _topsDirty = true;
        RecomputeTops();
        _coveredBottom = -1;
        InvalidateMeasure();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scrollViewer = null;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var previous = _viewport;
        if (e.EffectiveViewport == previous) return;
        _viewport = e.EffectiveViewport;

        if (IsCoveredByRealizedArea(previous, _viewport)) return;
        InvalidateMeasure();
    }

    private bool IsCoveredByRealizedArea(Rect previous, Rect viewport)
    {
        if (_pendingAnchor is not null || _pendingScrollDelta != 0 || _coveredBottom < 0) return false;
        if (viewport.Height <= 0 || viewport.Width != previous.Width || viewport.Height != previous.Height) return false;

        double total = _tops[^1];
        bool topCovered = viewport.Y - RemeasureMargin >= _coveredTop || _coveredTop <= 0.5;
        bool bottomCovered = viewport.Bottom + RemeasureMargin <= _coveredBottom || _coveredBottom >= total - 0.5;
        return topCovered && bottomCovered;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : _scrollViewer?.Viewport.Width ?? 0;

        if (width != _measureWidth)
        {
            _measureWidth = width;
            if (_knownHeights.Count > 0)
            {
                _knownHeights.Clear();
                Array.Fill(_heights, double.NaN);
                _topsDirty = true;
            }
        }

        int n = _items.Count;
        if (n == 0)
        {
            foreach (var control in _realized.Values) Recycle(control);
            _realized.Clear();
            foreach (var control in _reusable.Values) Recycle(control);
            _reusable.Clear();
            _pendingAnchor = null;
            _topsDirty = true;
            RecomputeTops();
            _coveredBottom = -1;
            return default;
        }

        RecomputeTops();

        var viewport = CurrentViewport(width);

        if (_pendingAnchor is { } anchor)
        {
            _pendingAnchor = null;
            if (TryResolveAnchor(anchor, out int anchorIndex, out double relativeTop))
            {
                double targetY = Math.Max(0, _tops[anchorIndex] - relativeTop);
                _pendingScrollDelta += targetY - viewport.Y;
                viewport = new Rect(viewport.X, targetY, viewport.Width, viewport.Height);
            }
        }

        double buffer = Math.Max(MinBuffer, viewport.Height * 0.5);
        double from = Math.Max(0, viewport.Y - buffer);
        double to = viewport.Bottom + buffer;

        int visibleFirst = FindIndexAt(Math.Max(0, viewport.Y));
        double visibleFirstTopBefore = _tops[visibleFirst];

        foreach (var (index, control) in _realized)
        {
            if (!_reusable.TryAdd(_items[index], control)) Recycle(control);
        }
        _realized.Clear();

        var measureSize = new Size(width, double.PositiveInfinity);
        int first = FindIndexAt(from);
        double y = _tops[first];
        for (int i = first; i < n && y < to; i++)
        {
            var control = Realize(i);
            if (control is null)
            {
                y += EffectiveHeight(i);
                continue;
            }

            control.Measure(measureSize);
            RecordHeight(i, control.DesiredSize.Height);
            y += _heights[i];
        }

        foreach (var control in _reusable.Values) Recycle(control);
        _reusable.Clear();

        RecomputeTops();
        _coveredTop = _tops[first];
        _coveredBottom = y;
        double correction = _tops[visibleFirst] - visibleFirstTopBefore;
        if (Math.Abs(correction) > 0.01) _pendingScrollDelta += correction;

        return new Size(0, _tops[n]);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, control) in _realized)
        {
            double height = double.IsNaN(_heights[index]) ? control.DesiredSize.Height : _heights[index];
            control.Arrange(new Rect(0, _tops[index], finalSize.Width, height));
        }
        return finalSize;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_pendingScrollDelta == 0 || _scrollViewer is not { } scrollViewer) return;

        double delta = _pendingScrollDelta;
        _pendingScrollDelta = 0;

        double maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        double newY = Math.Clamp(scrollViewer.Offset.Y + delta, 0, maxY);
        if (Math.Abs(newY - scrollViewer.Offset.Y) > 0.01)
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newY);
    }

    private Control? Realize(int index)
    {
        var item = _items[index];
        if (_reusable.Remove(item, out var existing))
        {
            _realized[index] = existing;
            return existing;
        }

        bool isHeader = _isHeader[index];
        var pool = isHeader ? _headerPool : _rowPool;

        Control? control;
        if (pool.Count > 0)
        {
            control = pool.Pop();
            control.DataContext = item;
            control.IsVisible = true;
        }
        else
        {
            control = (isHeader ? HeaderTemplate : RowTemplate)?.Build(item);
            if (control is null) return null;
            _controlIsHeader[control] = isHeader;
            control.DataContext = item;
            Children.Add(control);
        }

        _realized[index] = control;
        return control;
    }

    private void Recycle(Control control)
    {
        control.DataContext = null;

        bool isHeader = _controlIsHeader.TryGetValue(control, out bool header) && header;
        var pool = isHeader ? _headerPool : _rowPool;
        if (pool.Count < MaxPooledPerKind)
        {
            control.IsVisible = false;
            pool.Push(control);
        }
        else
        {
            Children.Remove(control);
            _controlIsHeader.Remove(control);
        }
    }

    private void RecordHeight(int index, double height)
    {
        double old = _heights[index];
        if (old == height) return;

        if (_isHeader[index])
        {
            if (double.IsNaN(old)) _headerHeightCount++; else _headerHeightSum -= old;
            _headerHeightSum += height;
        }
        else
        {
            if (double.IsNaN(old)) _rowHeightCount++; else _rowHeightSum -= old;
            _rowHeightSum += height;
        }

        _heights[index] = height;
        _knownHeights[_items[index]] = height;
        _topsDirty = true;
    }

    private double EffectiveHeight(int index)
    {
        double height = _heights[index];
        if (!double.IsNaN(height)) return height;

        return _isHeader[index]
            ? (_headerHeightCount > 0 ? _headerHeightSum / _headerHeightCount : DefaultHeaderHeight)
            : (_rowHeightCount > 0 ? _rowHeightSum / _rowHeightCount : DefaultRowHeight);
    }

    private void RecomputeTops()
    {
        int n = _items.Count;
        if (!_topsDirty && _tops.Length == n + 1) return;
        _topsDirty = false;
        if (_tops.Length != n + 1) _tops = new double[n + 1];

        double y = 0;
        for (int i = 0; i < n; i++)
        {
            _tops[i] = y;
            y += EffectiveHeight(i);
        }
        _tops[n] = y;
    }

    private int FindIndexAt(double y)
    {
        int n = _items.Count;
        if (n == 0) return 0;

        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_tops[mid] <= y) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private Rect CurrentViewport(double width)
    {
        var viewport = _viewport;
        if (viewport.Height <= 0)
        {
            double fallbackHeight = _scrollViewer is { Viewport.Height: > 0 } scrollViewer ? scrollViewer.Viewport.Height : 1000;
            viewport = new Rect(0, Math.Max(0, viewport.Y), width, fallbackHeight);
        }
        return new Rect(viewport.X, Math.Max(0, viewport.Y + _pendingScrollDelta), viewport.Width, viewport.Height);
    }

    private List<(string[] Keys, double RelativeTop)>? CaptureAnchor()
    {
        if (_items.Count == 0 || AnchorKeySelector is null || _tops.Length != _items.Count + 1) return null;

        var viewport = CurrentViewport(_measureWidth);

        // At the very top there is nothing that could shift — staying at 0 is correct.
        if (_viewport.Height <= 0 || viewport.Y <= 0.5) return null;

        var candidates = new List<(string[] Keys, double RelativeTop)>();
        for (int i = FindIndexAt(viewport.Y); i < _items.Count && _tops[i] < viewport.Bottom; i++)
        {
            string[] keys = [.. AnchorKeySelector(_items[i])];
            if (keys.Length > 0) candidates.Add((keys, _tops[i] - viewport.Y));
            if (candidates.Count >= MaxAnchorCandidates) break;
        }
        return candidates.Count > 0 ? candidates : null;
    }

    private bool TryResolveAnchor(List<(string[] Keys, double RelativeTop)> anchor, out int index, out double relativeTop)
    {
        index = -1;
        relativeTop = 0;
        if (AnchorKeySelector is null) return false;

        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _items.Count; i++)
        {
            foreach (string key in AnchorKeySelector(_items[i]))
                indexByKey.TryAdd(key, i);
        }

        foreach (var (keys, top) in anchor)
        {
            foreach (string key in keys)
            {
                if (!indexByKey.TryGetValue(key, out int found)) continue;
                index = found;
                relativeTop = top;
                return true;
            }
        }
        return false;
    }
}
