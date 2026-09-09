using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Models;

internal class LiveryGroup : INotifyPropertyChanged, IDisposable
{
    private string _key = "";
    public required string Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value;
            OnPropertyChanged();
        }
    }

    private List<LiveryEntry> _items = [];
    public required List<LiveryEntry> Items
    {
        get => _items;
        init
        {
            _items = value;
            foreach (var entry in _items)
                entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    public int Count => Items.Count;
    public int FavoriteCount => Items.Count(x => x.IsFavorite);
    public int DuplicateCount => Items.Count(x => x.IsDuplicate);
    public int PossibleDuplicateCount => Items.Count(x => x.IsPossibleDuplicate);

    public bool HasFavorites => FavoriteCount > 0;
    public bool HasDuplicates => DuplicateCount > 0;
    public bool HasPossibleDuplicates => PossibleDuplicateCount > 0;

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveryEntry.IsFavorite)) return;
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(HasFavorites));
    }

    public void Dispose()
    {
        foreach (var entry in _items)
            entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    public bool IsFavoritesGroup { get; init; }

    private double _groupWidth = 1200;
    public double GroupWidth
    {
        get => _groupWidth;
        set
        {
            if (_groupWidth == value) return;
            _groupWidth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Rows));
        }
    }

    private const double CardStep = 286;

    private List<GalleryRow>? _rows;
    private double _rowsBuiltForWidth = -1;

    public IReadOnlyList<GalleryRow> Rows
    {
        get
        {
            if (_rows is not null && _rowsBuiltForWidth == GroupWidth) return _rows;

            int columns = Math.Max(1, (int)(GroupWidth / CardStep));
            var rows = new List<GalleryRow>(Items.Count / columns + 1);
            for (int i = 0; i < Items.Count; i += columns)
                rows.Add(new GalleryRow { Items = Items.GetRange(i, Math.Min(columns, Items.Count - i)) });

            _rows = rows;
            _rowsBuiltForWidth = GroupWidth;
            return _rows;
        }
    }

    public string CountText => $"({Count})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
