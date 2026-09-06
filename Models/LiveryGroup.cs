using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Models;

internal class LiveryGroup : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required List<LiveryEntry> Items { get; init; }
    public int Count => Items.Count;
    public int FavoriteCount => Items.Count(x => x.IsFavorite);
    private double _groupWidth = 1200;
    public double GroupWidth
    {
        get => _groupWidth;
        set
        {
            if (_groupWidth == value) return;
            _groupWidth = value;
            OnPropertyChanged();
        }
    }

    public string CountText => FavoriteCount > 0 && FavoriteCount < Count
        ? $"({Count}, ⭐{FavoriteCount})"
        : $"({Count})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
