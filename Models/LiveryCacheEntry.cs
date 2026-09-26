using LiveryGallery.Enums;

namespace LiveryGallery.Models;

internal record LiveryCacheEntry
{
    public const int CurrentSchemaVersion = 7;
    public int SchemaVersion { get; init; }
    public string FolderName { get; init; } = string.Empty;
    public string LiveryName { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string? AuthorIdentityTagHex { get; init; }
    public ulong? CreatorUserId { get; init; }
    public bool IsPossiblyGenerated { get; init; }
    public int GenerationAlgorithmVersion { get; init; }
    public bool HasNoLayers { get; init; }
    public bool HasParseError { get; init; }
    public string? ParserVersion { get; init; }
    public IReadOnlyList<LiveryParseIssue>? ParseIssues { get; init; }
    public uint? CLiveryCarId { get; init; }
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
    public DuplicateStatus DuplicateStatus { get; init; } = DuplicateStatus.Ok;
    public IReadOnlyList<DuplicateRelation>? PossibleDuplicateOf { get; init; }
}
