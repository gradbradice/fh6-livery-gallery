using LiveryGallery.Enums;
using LiveryGallery.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveryGallery.ViewModels;

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
    public int MineCount => Items.Count(x => x.IsMine);
    public int DuplicateCount => Items.Count(x => x.IsDuplicate);
    public int PossibleDuplicateCount => Items.Count(x => x.IsPossibleDuplicate);

    public bool HasFavorites => FavoriteCount > 0;
    public bool HasMine => MineCount > 0;
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
    public bool IsMineGroup { get; init; }

    public LiveryGroupSpecialKind SpecialKind { get; init; } = LiveryGroupSpecialKind.None;
    public DateTime? SpecialMonth { get; init; }
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

    private LazyRowList? _rows;
    private double _rowsBuiltForWidth = -1;

    public IReadOnlyList<GalleryRow> Rows
    {
        get
        {
            if (_rows is not null && _rowsBuiltForWidth == GroupWidth) return _rows;

            int columns = Math.Max(1, (int)(GroupWidth / CardStep));
            _rows = new LazyRowList(Items, columns);
            _rowsBuiltForWidth = GroupWidth;
            return _rows;
        }
    }

    public string CountText => $"({Count})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
