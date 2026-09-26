using LiveryGallery.ViewModels;

namespace LiveryGallery.Models;

internal sealed class GalleryRow
{
    public required IReadOnlyList<LiveryEntry> Items { get; init; }
    public required LiveryGroup Group { get; init; }
    public bool IsLastInGroup { get; init; }
}
