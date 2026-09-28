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
    private volatile AuthorNameInference _authorInference = AuthorNameInference.Empty;

    public AuthorNameInference AuthorInference => _authorInference;

    public void ReconcileAuthorCards(IEnumerable<LiveryCacheEntry> cacheEntries)
    {
        var list = cacheEntries as IReadOnlyCollection<LiveryCacheEntry> ?? [.. cacheEntries];
        _authorInference = AuthorNameInference.Build(list);
        authorCardService.ReconcileAutoCards(list
            .Where(c => !AuthorNameInference.IsAuthorMissing(c))
            .Select(c => new AuthorObservation(c.Author, c.AuthorIdentityTagHex, c.DownloadDate)));
    }

    public (string Name, bool IsInferred, bool IsUnknown) ResolveRawAuthor(LiveryCacheEntry c) =>
        _authorInference.Resolve(c, tag => authorCardService.FindCardForIdentityTag(tag)?.KnownNames
            .LastOrDefault(n => !string.IsNullOrWhiteSpace(n)));

    public void EnsureLiveryIds(IEnumerable<LiveryCacheEntry> cacheEntries) =>
        liveryIdService.EnsureAssigned(cacheEntries.Select(c => (c.FolderName, c.DownloadDate)));

    public LiveryEntry ToEntry(LiveryCacheEntry c, ulong? currentUserId)
    {
        var car = carDatabaseService.Get(c.CarId);
        var (rawAuthor, isAuthorInferred, isAuthorUnknown) = ResolveRawAuthor(c);
        var data = new LiveryData
        {
            FolderName = c.FolderName,
            LiveryName = PlaceholderTexts.IsNameMissing(c) ? string.Empty : c.LiveryName,
            TextVersion = LiveryCacheEntry.CurrentTextVersion,
            AuthorRaw = rawAuthor,
            IsAuthorInferred = isAuthorInferred,
            IsAuthorUnknown = isAuthorUnknown,
            AuthorIdentityTagHex = c.AuthorIdentityTagHex,
            CreatorUserId = c.CreatorUserId,
            IsPossiblyGenerated = c.IsPossiblyGenerated,
            HasNoLayers = c.HasNoLayers,
            HasParseError = c.HasParseError,
            ParseIssues = c.ParseIssues,
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
            ExternalPreviewPath = c.ExternalThumbSource,
            CLiveryHash = c.CLiveryHash,
            LiveryId = liveryIdService.TryGet(c.FolderName) ?? 0,
            RelatedLiveryIds = BuildRelatedLiveryIds(c.PossibleDuplicateOf),
        };

        return new LiveryEntry
        {
            Data = data,
            Author = authorCardService.ResolveDisplayName(rawAuthor, c.AuthorIdentityTagHex),
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
