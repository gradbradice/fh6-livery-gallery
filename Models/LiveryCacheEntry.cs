using LiveryGallery.Enums;

namespace LiveryGallery.Models;

internal record LiveryCacheEntry
{
    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public string LiveryName { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public int CarId { get; init; }
    public LiveryConsistency CarIdConsistency { get; init; } = LiveryConsistency.Consistent;
    public int CreatedYear { get; init; }
    public int CreatedMonth { get; init; }
    public DateTime? DownloadDate { get; init; }
    public string? ThumbnailFile { get; init; }
    public string HeaderHash { get; init; } = string.Empty;
    public string? SourceThumbHash { get; init; }
    public string? CLiveryHash { get; init; }
    public long HeaderLength { get; init; }
    public DateTime HeaderLastWriteUtc { get; init; }
    public long SourceThumbLength { get; init; }
    public DateTime SourceThumbLastWriteUtc { get; init; }
    public long CLiveryLength { get; init; }
    public DateTime CLiveryLastWriteUtc { get; init; }
    public uint[]? SectionCounts { get; init; }
}
