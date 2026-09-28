using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.Text.Json.Serialization;
namespace LiveryGallery.Models;

internal sealed record LiveryData
{
    public required string FolderName { get; init; }
    public required string LiveryName { get; init; }
    public required string AuthorRaw { get; init; }
    public string? AuthorIdentityTagHex { get; init; }
    public bool IsAuthorInferred { get; init; }
    public bool IsAuthorUnknown { get; init; }
    public int TextVersion { get; init; }
    public ulong? CreatorUserId { get; init; }
    public bool IsPossiblyGenerated { get; init; }
    public bool HasNoLayers { get; init; }
    public bool HasParseError { get; init; }
    public IReadOnlyList<LiveryParseIssue>? ParseIssues { get; init; }
    public ulong LiveryId { get; init; }
    [JsonIgnore]
    public IReadOnlyDictionary<string, ulong>? RelatedLiveryIds { get; init; }
    public required int CarId { get; init; }
    public required string CarManufacturerRaw { get; init; }
    public required string CarModelNameRaw { get; init; }
    public int? CarYear { get; init; }
    public bool CarKnown { get; init; }
    public int? CreatedYear { get; init; }
    public int? CreatedMonth { get; init; }
    public DateTime? DownloadDate { get; init; }
    public string? ThumbnailPath { get; init; }
    public string? CLiveryHash { get; init; }
    public required bool HasThumbnail { get; init; }
    [JsonIgnore]
    public string? ExternalPreviewPath { get; init; }
    [JsonIgnore]
    public bool IsAuction => LiveryFolders.IsAuction(FolderName);

    [JsonIgnore]
    private bool IsLegacyText => TextVersion < LiveryCacheEntry.CurrentTextVersion;

    [JsonIgnore]
    public string DisplayLiveryName =>
        string.IsNullOrEmpty(LiveryName) || (IsLegacyText && PlaceholderTexts.IsNoLiveryName(LiveryName))
            ? Strings.LiveryNoName
            : LiveryName;

    [JsonIgnore]
    public string DisplayAuthorRaw =>
        IsAuthorUnknown || string.IsNullOrEmpty(AuthorRaw) || (IsLegacyText && PlaceholderTexts.IsUnknownAuthor(AuthorRaw))
            ? Strings.UnknownAuthor
            : AuthorRaw;
}
