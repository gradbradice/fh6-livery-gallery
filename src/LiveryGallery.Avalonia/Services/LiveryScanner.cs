using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.Configuration;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.ViewModels;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LiveryGallery.Services;

/// <summary>
/// The parts of the scan the host gives, from the closed ForzaToolkit.Liveries.Formats: the parser of the game's
/// files and its duplicate check (null: no duplicate check)
/// </summary>
internal sealed record LiveryScanParts(ILiveryParser Parser, ILiveryDuplicateDetector? Duplicates);

/// <summary>
/// The gallery's scan: <see cref="LiveryLibraryScanner"/> of ForzaToolkit.Liveries with the game's parts (the
/// profile's livery store, the parser and duplicate check the host gives, previews) and the gallery's own steps
/// (author cards, livery numbers, archived previews, texts of older versions). It turns the scanned cache into
/// <see cref="LiveryEntry"/> view models and words progress.
/// </summary>
internal sealed class LiveryScanner
{
    private static readonly int ScanParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    private readonly AppCacheService _appCacheService;
    private readonly AuthorCardService _authorCardService;
    private readonly LiveryEntryFactory _entryFactory;
    private readonly LiveryLibraryScanner _scanner;

    public LiveryScanner(
        AppCacheService appCacheService,
        CarDatabaseService carDatabaseService,
        FavoriteService favoriteService,
        TagService tagService,
        AuthorCardService authorCardService,
        LiveryArchive archiveService,
        SaveFolderLock saveFolderLock,
        LiveryIdService liveryIdService,
        GameProfile profile,
        LiveryScanParts parts)
    {
        _appCacheService = appCacheService;
        _authorCardService = authorCardService;
        _entryFactory = new LiveryEntryFactory(
            appCacheService, carDatabaseService, favoriteService, tagService, authorCardService, liveryIdService, profile);
        _scanner = new LiveryLibraryScanner(new LiveryScannerOptions
        {
            Cache = appCacheService,
            Store = profile.Liveries,
            Parser = parts.Parser,
            Previews = new ThumbnailPreviewWriter(),
            Duplicates = parts.Duplicates,
            SaveFolderLock = saveFolderLock,
            RemovedPreviewDirectory = AppSettings.ChangesPreviewsPath,
            Parallelism = ScanParallelism,
            Logger = AppLogger.LoggerFactory.CreateLogger<LiveryLibraryScanner>(),
            Hooks = new LiveryScanHooks
            {
                NeedsReparse = PlaceholderTexts.NeedsTextMigration,
                CacheScanned = _entryFactory.ReconcileAuthorCards,
                DuplicateGroupAuthor = ResolvedAuthor,
                // With the detector's version in front, the same text as before the move: saved signatures stay valid
                DuplicateSignature = entries =>
                    $"{_authorCardService.GetIdentityMappingFingerprint()}|{_entryFactory.AuthorInference.Fingerprint(entries)}",
                DuplicatesUpdated = _entryFactory.EnsureLiveryIds,
                ReferencedPreviews = archiveService.GetReferencedThumbnailFiles,
            },
        });
    }

    private string ResolvedAuthor(LiveryCacheEntry entry) =>
        _authorCardService.ResolveDisplayName(_entryFactory.ResolveRawAuthor(entry).Name, entry.AuthorIdentityTagHex);

    /// <param name="collectChanges">
    /// Compare with the cache the previous launch saved and report what was added and removed. Only the first
    /// scan of a launch should ask: later scans compare with this launch's own previous scan.
    /// </param>
    /// <param name="accountKey">Which account's cache to use (see <see cref="AccountKey"/>)</param>
    public async Task<LiveryScanEntry> ScanAsync(
        string accountKey, string savePath, ulong? currentUserId, bool needEntries, bool collectChanges,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var words = progress is null ? null : new Progress(progress);
        var result = await _scanner.ScanAsync(accountKey, savePath, collectChanges, words, ct);
        return await Task.Run(() => ToScanEntry(result, currentUserId, needEntries, ct), ct);
    }

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(string accountKey, ulong? currentUserId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var (cache, _) = _appCacheService.Load(accountKey);
            return _entryFactory.BuildEntries(cache.Values, currentUserId, ct);
        }, ct);

    /// <summary>The next scan cleans up orphaned previews even if its cache did not change (after an account was forgotten)</summary>
    public void RequestOrphanCleanup() => _scanner.RequestOrphanCleanup();

    private LiveryScanEntry ToScanEntry(LiveryScanResult result, ulong? currentUserId, bool needEntries, CancellationToken ct)
    {
        List<LiveryEntry> entries = [];
        if (result.CacheChanged || needEntries)
        {
            var built = new ConcurrentBag<LiveryEntry>();
            Parallel.ForEach(result.Cache.Values, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = ScanParallelism,
            }, cacheEntry => built.Add(_entryFactory.ToEntry(cacheEntry, currentUserId)));
            entries = [.. built];
        }

        return new LiveryScanEntry
        {
            Entries = entries,
            ReusedFromCache = result.ReusedFromCache,
            Parsed = result.Parsed,
            Errors = result.Errors,
            Removed = result.Removed,
            Added = result.Added,
            CacheChanged = result.CacheChanged,
            ChangesSinceLastLaunch = result.Changes is { } changes ? ToChanges(changes, currentUserId) : null,
        };
    }

    private LiveryChanges ToChanges(LiveryScanChanges changes, ulong? currentUserId)
    {
        var added = changes.Added
            .Select(e => _entryFactory.ToEntry(e, currentUserId))
            .OrderByDescending(e => e.DownloadDate ?? DateTime.MinValue)
            .ThenBy(e => e.LiveryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var removed = changes.Removed
            .Select(r => new RemovedLivery(_entryFactory.ToEntry(r.Entry, currentUserId), r.PreviewPath))
            .ToList();
        removed.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Entry.LiveryName, b.Entry.LiveryName));

        return new LiveryChanges(added, removed);
    }

    /// <summary>Words the toolkit's progress in the user's language</summary>
    private sealed class Progress(IProgress<string> target) : IProgress<LiveryScanProgress>
    {
        public void Report(LiveryScanProgress value) => target.Report(value.Stage switch
        {
            LiveryScanStage.SavingCache => Strings.LoadingSavingCache,
            _ => string.Format(Strings.ParsingProgress, value.Done, value.Total, value.Key),
        });
    }
}
