using System.Collections;

namespace LiveryGallery.Models;

internal sealed class ListSlice<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _source;
    private readonly int _offset;

    public ListSlice(IReadOnlyList<T> source, int offset, int count)
    {
        _source = source;
        _offset = offset;
        Count = count;
    }

    public int Count { get; }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _source[_offset + index];
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return _source[_offset + i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
