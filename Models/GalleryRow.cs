namespace LiveryGallery.Models;

internal sealed class GalleryRow
{
    public required IReadOnlyList<LiveryEntry> Items { get; init; }
}
