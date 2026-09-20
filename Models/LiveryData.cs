namespace LiveryGallery.Models;

internal sealed record LiveryData
{
    public required string FolderName { get; init; }
    public required string LiveryName { get; init; }
    public required string AuthorRaw { get; init; }
    public string? AuthorIdentityTagHex { get; init; }
    public ulong? CreatorUserId { get; init; }
    public bool IsPossiblyGenerated { get; init; }
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
}
