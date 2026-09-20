using LiveryGallery.Models;
using LiveryGallery.ViewModels;

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
        var data = new LiveryData
        {
            FolderName = c.FolderName,
            LiveryName = c.LiveryName,
            AuthorRaw = c.Author,
            AuthorIdentityTagHex = c.AuthorIdentityTagHex,
            CreatorUserId = c.CreatorUserId,
            IsPossiblyGenerated = c.IsPossiblyGenerated,
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
