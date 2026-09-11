using System.Collections;

namespace LiveryGallery.Models;

internal sealed class LazyRowList : IReadOnlyList<GalleryRow>
{
    private readonly IReadOnlyList<LiveryEntry> _items;
    private readonly int _columns;
    private readonly GalleryRow?[] _cache;

    public LazyRowList(IReadOnlyList<LiveryEntry> items, int columns)
    {
        _items = items;
        _columns = Math.Max(1, columns);
        Count = _items.Count == 0 ? 0 : (_items.Count + _columns - 1) / _columns;
        _cache = new GalleryRow?[Count];
    }

    public int Count { get; }

    public GalleryRow this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_cache[index] is { } cached) return cached;

            int start = index * _columns;
            int count = Math.Min(_columns, _items.Count - start);
            var row = new GalleryRow { Items = new ListSlice<LiveryEntry>(_items, start, count) };
            _cache[index] = row;
            return row;
        }
    }

    public IEnumerator<GalleryRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
