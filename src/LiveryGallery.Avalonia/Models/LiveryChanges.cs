using LiveryGallery.ViewModels;

namespace LiveryGallery.Models;

/// <param name="Entry">Built from the cache entry the previous launch left behind</param>
/// <param name="PreviewPath">A copy of the preview kept outside the thumbnail cache (the scan deletes the original), or null</param>
internal sealed record RemovedLivery(LiveryEntry Entry, string? PreviewPath);

/// <summary>What appeared in and disappeared from the save folder while the gallery was closed</summary>
internal sealed record LiveryChanges(IReadOnlyList<LiveryEntry> Added, IReadOnlyList<RemovedLivery> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
}
