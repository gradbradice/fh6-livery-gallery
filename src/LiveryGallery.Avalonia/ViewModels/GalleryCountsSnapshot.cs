namespace LiveryGallery.ViewModels;

internal readonly record struct GalleryCountsSnapshot(List<LiveryEntry> FilteredEntries, int TotalCount, string? SearchText);
