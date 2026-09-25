using LiveryGallery.Models;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

internal sealed class LiveryEntryFactory(
    AppCacheService appCacheService,
    CarDatabaseService carDatabaseService,
    FavoriteService favoriteService,
    TagService tagService,
    AuthorCardService authorCardService,
    LiveryIdService liveryIdService)
{
    public void ReconcileAuthorCards(IEnumerable<LiveryCacheEntry> cacheEntries) =>
        authorCardService.ReconcileAutoCards(cacheEntries.Select(c =>
            new AuthorObservation(c.Author, c.AuthorIdentityTagHex, c.DownloadDate)));

    public void EnsureLiveryIds(IEnumerable<LiveryCacheEntry> cacheEntries) =>
        liveryIdService.EnsureAssigned(cacheEntries.Select(c => (c.FolderName, c.DownloadDate)));

    public LiveryEntry ToEntry(LiveryCacheEntry c, ulong? currentUserId)
    {
        var car = carDatabaseService.Get(c.CarId);
        var data = new LiveryData
        {
            FolderName = c.FolderName,
            LiveryName = c.LiveryName,
            AuthorRaw = c.Author,
            AuthorIdentityTagHex = c.AuthorIdentityTagHex,
            CreatorUserId = c.CreatorUserId,
            IsPossiblyGenerated = c.IsPossiblyGenerated,
            HasNoLayers = c.HasNoLayers,
            HasParseError = c.HasParseError,
            CarId = c.CarId,
            CarManufacturerRaw = car?.Manufacturer ?? string.Empty,
            CarModelNameRaw = car?.Name ?? string.Empty,
            CarYear = car?.Year,
            CarKnown = car is not null,
            CreatedYear = c.CreatedYear,
            CreatedMonth = c.CreatedMonth,
            DownloadDate = c.DownloadDate,
            ThumbnailPath = c.ThumbnailFile is not null
                ? Path.Combine(appCacheService.ThumbsDir, c.ThumbnailFile)
                : null,
            HasThumbnail = c.ThumbnailFile is not null,
            CLiveryHash = c.CLiveryHash,
            LiveryId = liveryIdService.TryGet(c.FolderName) ?? 0,
            RelatedLiveryIds = BuildRelatedLiveryIds(c.PossibleDuplicateOf),
        };

        return new LiveryEntry
        {
            Data = data,
            Author = authorCardService.ResolveDisplayName(c.Author, c.AuthorIdentityTagHex),
            IsMine = currentUserId is not null && c.CreatorUserId == currentUserId,
            Tags = tagService.GetTags(c.FolderName),
            IsFavorite = favoriteService.IsFavorite(c.FolderName),
            DuplicateStatus = c.DuplicateStatus,
            PossibleDuplicateOf = c.PossibleDuplicateOf,
        };
    }

    private Dictionary<string, ulong>? BuildRelatedLiveryIds(IReadOnlyList<DuplicateRelation>? relations)
    {
        if (relations is not { Count: > 0 }) return null;
        var result = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in relations)
            if (liveryIdService.TryGet(r.Id) is { } id) result[r.Id] = id;
        return result;
    }

    public List<LiveryEntry> BuildEntries(IEnumerable<LiveryCacheEntry> cacheEntries, ulong? currentUserId, CancellationToken ct)
    {
        var cacheList = cacheEntries as IReadOnlyCollection<LiveryCacheEntry> ?? [.. cacheEntries];
        ReconcileAuthorCards(cacheList);
        EnsureLiveryIds(cacheList);
        var entries = new List<LiveryEntry>();
        foreach (var cacheEntry in cacheList)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(ToEntry(cacheEntry, currentUserId));
        }
        return entries;
    }
}
