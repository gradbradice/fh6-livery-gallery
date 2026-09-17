using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal sealed class LiveryEntryFactory(
    AppCacheService appCacheService,
    CarDatabaseService carDatabaseService,
    FavoriteService favoriteService,
    TagService tagService,
    AuthorCardService authorCardService)
{
    public void ReconcileAuthorCards(IEnumerable<LiveryCacheEntry> cacheEntries) =>
        authorCardService.ReconcileAutoCards(cacheEntries.Select(c =>
            new AuthorObservation(c.Author, c.AuthorIdentityTagHex, c.DownloadDate)));

    public LiveryEntry ToEntry(LiveryCacheEntry c, ulong? currentUserId)
    {
        var car = carDatabaseService.Get(c.CarId);
        return new LiveryEntry
        {
            FolderName = c.FolderName,
            LiveryName = c.LiveryName,
            AuthorRaw = c.Author,
            AuthorIdentityTagHex = c.AuthorIdentityTagHex,
            Author = authorCardService.ResolveDisplayName(c.Author, c.AuthorIdentityTagHex),
            CreatorUserId = c.CreatorUserId,
            IsPossiblyGenerated = c.IsPossiblyGenerated,
            IsMine = currentUserId is not null && c.CreatorUserId == currentUserId,
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
            Tags = tagService.GetTags(c.FolderName),
            IsFavorite = favoriteService.IsFavorite(c.FolderName),
            CLiveryHash = c.CLiveryHash,
            DuplicateStatus = c.DuplicateStatus,
            PossibleDuplicateOf = c.PossibleDuplicateOf,
        };
    }

    public List<LiveryEntry> BuildEntries(IEnumerable<LiveryCacheEntry> cacheEntries, ulong? currentUserId, CancellationToken ct)
    {
        var cacheList = cacheEntries as IReadOnlyCollection<LiveryCacheEntry> ?? [.. cacheEntries];
        ReconcileAuthorCards(cacheList);
        var entries = new List<LiveryEntry>();
        foreach (var cacheEntry in cacheList)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(ToEntry(cacheEntry, currentUserId));
        }
        return entries;
    }
}
