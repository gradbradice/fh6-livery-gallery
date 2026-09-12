using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal sealed class LiveryEntryFactory(
    AppCacheService appCacheService,
    CarDatabaseService carDatabaseService,
    FavoriteService favoriteService,
    TagService tagService,
    AuthorCardService authorCardService)
{
    public LiveryEntry ToEntry(LiveryCacheEntry c)
    {
        var car = carDatabaseService.Get(c.CarId);
        return new LiveryEntry
        {
            FolderPath = c.FolderPath,
            FolderName = c.FolderName,
            LiveryName = c.LiveryName,
            AuthorRaw = c.Author,
            Author = authorCardService.ResolveDisplayName(c.Author),
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
            SectionCounts = c.SectionCounts
        };
    }

    public List<LiveryEntry> BuildEntries(IEnumerable<LiveryCacheEntry> cacheEntries, CancellationToken ct)
    {
        var entries = new List<LiveryEntry>();
        foreach (var cacheEntry in cacheEntries)
        {
            ct.ThrowIfCancellationRequested();
            entries.Add(ToEntry(cacheEntry));
        }
        LiveryDuplicateService.MarkDuplicates(entries);
        return entries;
    }
}
