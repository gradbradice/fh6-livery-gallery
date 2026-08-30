namespace LiveryGallery.Models;

internal class LiveryGroup
{
    public required string Key { get; init; }
    public required List<LiveryEntry> Items { get; init; }
    public int Count => Items.Count;
    public int FavoriteCount => Items.Count(x => x.IsFavorite);
    public double GroupWidth { get; set; } = 1200;

    public string CountText => FavoriteCount > 0 && FavoriteCount < Count
        ? $"({Count}, ⭐{FavoriteCount})"
        : $"({Count})";
}
