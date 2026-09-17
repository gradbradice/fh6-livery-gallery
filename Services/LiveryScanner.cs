using ForzaData;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Collections.Concurrent;

namespace LiveryGallery.Services;

internal sealed class LiveryScanner
{
    private readonly AppCacheService _appCacheService;
    private readonly AuthorCardService _authorCardService;
    private readonly LiveryEntryFactory _entryFactory;
    private readonly LiveryReader _reader;

    public LiveryScanner(
        AppCacheService appCacheService,
        CarDatabaseService carDatabaseService,
        FavoriteService favoriteService,
        TagService tagService,
        AuthorCardService authorCardService)
    {
        _appCacheService = appCacheService;
        _authorCardService = authorCardService;
        _entryFactory = new LiveryEntryFactory(appCacheService, carDatabaseService, favoriteService, tagService, authorCardService);
        _reader = new LiveryReader(appCacheService);
    }

    public Task<LiveryScanEntry> ScanAsync(
        string savePath, ulong? currentUserId, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Scan(savePath, currentUserId, progress, ct), ct);

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(ulong? currentUserId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var (cache, _) = _appCacheService.Load();
            return _entryFactory.BuildEntries(cache.Values, currentUserId, ct);
        }, ct);

    private LiveryScanEntry Scan(string savePath, ulong? currentUserId, IProgress<string>? progress, CancellationToken ct)
    {
        var (oldCache, oldCacheLoadFailed) = _appCacheService.Load();

        var folders = LocalSaveService.GetListLiveryDirs(savePath)
            .Select(name => Path.Combine(savePath, name))
            .ToList();

        var newCache = new ConcurrentDictionary<string, LiveryCacheEntry>();
        var freshlyParsedNames = new ConcurrentDictionary<string, byte>(); // используется как concurrent HashSet<string>

        int reused = 0, parsed = 0, errors = 0, done = 0;
        int total = folders.Count;

        Parallel.ForEach(folders, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
        }, folder =>
        {
            int myDone = Interlocked.Increment(ref done);
            string folderKey = Path.GetFileName(folder);

            LiveryCacheEntry? cacheEntry = null;
            try
            {
                cacheEntry = TryReuseOrParse(folder, oldCache, progress, myDone, total, out bool wasReused);
                if (wasReused)
                {
                    Interlocked.Increment(ref reused);
                }
                else
                {
                    Interlocked.Increment(ref parsed);
                    freshlyParsedNames[folderKey] = 0;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                AppLogger.LogErrorThrottled(folder, $"Scan error: {Path.GetFileName(folder)}", ex);
                if (oldCache.TryGetValue(folderKey, out var fallback))
                    cacheEntry = fallback;
            }

            if (cacheEntry is not null)
            {
                newCache[folderKey] = cacheEntry;
            }
        });

        var finalCache = new Dictionary<string, LiveryCacheEntry>(newCache);
        _entryFactory.ReconcileAuthorCards(finalCache.Values);
        UpdateDuplicateStatuses(finalCache, oldCache, [.. freshlyParsedNames.Keys], savePath);

        var entries = new ConcurrentBag<LiveryEntry>();
        Parallel.ForEach(finalCache.Values, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
        }, cacheEntry =>
        {
            entries.Add(_entryFactory.ToEntry(cacheEntry, currentUserId));
        });

        int removed = oldCache.Keys.Except(finalCache.Keys).Count();

        var entriesList = entries.ToList();
        bool cacheChanged = oldCache.Count != finalCache.Count
            || finalCache.Any(kv => !oldCache.TryGetValue(kv.Key, out var old) || old != kv.Value);

        if (cacheChanged)
        {
            progress?.Report(Strings.LoadingSavingCache);
            _appCacheService.Save(finalCache);
        }

        bool suspiciousDrop = oldCache.Count > 0 && removed > oldCache.Count * 0.2;
        if (errors == 0 && !suspiciousDrop && !oldCacheLoadFailed)
        {
            CleanupOrphanedThumbnails(finalCache);
        }
        else
        {
            AppLogger.LogErrorThrottled(savePath,
                $"Skipped orphaned thumbnail cleanup: {errors} scan errors, {removed} of {oldCache.Count} previously known liveries missing, cache load failed: {oldCacheLoadFailed}",
                new InvalidOperationException("Unreliable scan snapshot"));
        }

        return new LiveryScanEntry
        {
            Entries = entriesList,
            ReusedFromCache = reused,
            Parsed = parsed,
            Errors = errors,
            Removed = removed,
            CacheChanged = cacheChanged
        };
    }

    private void CleanupOrphanedThumbnails(Dictionary<string, LiveryCacheEntry> newCache)
    {
        try
        {
            var referenced = newCache.Values
                .Select(e => e.ThumbnailFile)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(_appCacheService.ThumbsDir, "*.png"))
            {
                if (referenced.Contains(Path.GetFileName(file))) continue;

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    AppLogger.LogErrorThrottled(file, $"Failed to delete orphaned preview '{file}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_appCacheService.ThumbsDir, "Failed to clear unused previews", ex);
        }
    }

    private LiveryCacheEntry TryReuseOrParse(
        string folder,
        Dictionary<string, LiveryCacheEntry> oldCache,
        IProgress<string>? progress,
        int done,
        int total,
        out bool wasReused)
    {
        bool found = oldCache.TryGetValue(Path.GetFileName(folder), out var existing);
        if (found && CanReuseCacheByStamp(folder, existing!))
        {
            wasReused = true;
            return existing!;
        }

        var hashes = _reader.CalculateFileHashes(folder, existing);

        if (CanReuseCache(existing, found, hashes))
        {
            wasReused = true;
            return RefreshStampsIfNeeded(existing!, hashes);
        }

        wasReused = false;
        int reportInterval = Math.Max(1, total / 50);
        if (done % reportInterval == 0 || done == total)
            progress?.Report(string.Format(Strings.ParsingProgress, done, total, Path.GetFileName(folder)));
        return _reader.ParseLivery(folder, hashes, existing);
    }

    private void UpdateDuplicateStatuses(
        Dictionary<string, LiveryCacheEntry> finalCache,
        Dictionary<string, LiveryCacheEntry> oldCache,
        HashSet<string> freshlyParsedNames,
        string savePath)
    {
        string ResolvedAuthor(LiveryCacheEntry e) => _authorCardService.ResolveDisplayName(e.Author, e.AuthorIdentityTagHex);

        var groups = finalCache.Values
            .GroupBy(e => (e.CarId, Author: ResolvedAuthor(e)), CarIdResolvedAuthorComparer.Instance)
            .ToList();

        var updates = new ConcurrentBag<(string Name, LiveryCacheEntry Entry)>();

        Parallel.ForEach(groups, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
        }, group =>
        {
            var members = group.ToList();

            if (members.Count <= 1)
            {
                var only = members[0];
                if (only.DuplicateStatus != DuplicateStatus.Ok || only.PossibleDuplicateOf is { Count: > 0 })
                    updates.Add((only.FolderName, only with { DuplicateStatus = DuplicateStatus.Ok, PossibleDuplicateOf = null }));
                return;
            }

            var currentNames = members.Select(m => m.FolderName).ToHashSet(StringComparer.Ordinal);
            var oldNames = oldCache.Values
                .Where(e => e.CarId == group.Key.CarId && string.Equals(ResolvedAuthor(e), group.Key.Author, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FolderName)
                .ToHashSet(StringComparer.Ordinal);

            bool membershipChanged = !currentNames.SetEquals(oldNames);
            bool anyMemberFresh = members.Any(m => freshlyParsedNames.Contains(m.FolderName));

            if (!membershipChanged && !anyMemberFresh)
            {
                foreach (var member in members)
                {
                    if (oldCache.TryGetValue(member.FolderName, out var old))
                        updates.Add((member.FolderName, member with
                        {
                            DuplicateStatus = old.DuplicateStatus,
                            PossibleDuplicateOf = old.PossibleDuplicateOf,
                        }));
                }
                return;
            }

            RecomputeDuplicatesForDirtyGroup(members, updates, savePath);
        });

        foreach (var (name, entry) in updates)
            finalCache[name] = entry;
    }

    private static void RecomputeDuplicatesForDirtyGroup(
        List<LiveryCacheEntry> members, ConcurrentBag<(string Name, LiveryCacheEntry Entry)> updates, string savePath)
    {
        var candidates = new List<LiveryDuplicateCandidate>(members.Count);
        foreach (var member in members)
        {
            uint[]? sectionCounts = LiveryReader.TryReadCLiverySectionCounts(Path.Combine(savePath, member.FolderName));
            candidates.Add(new LiveryDuplicateCandidate(member.FolderName, member.CarId, member.Author, member.CLiveryHash, sectionCounts));
        }

        var results = LiveryDuplicateDetector.Detect(candidates);

        foreach (var member in members)
        {
            if (!results.TryGetValue(member.FolderName, out var match)) continue;
            var status = match.Status switch
            {
                LiveryDuplicateStatus.Duplicate => DuplicateStatus.Duplicate,
                LiveryDuplicateStatus.PossibleDuplicate => DuplicateStatus.PossibleDuplicate,
                _ => DuplicateStatus.Ok,
            };
            updates.Add((member.FolderName, member with
            {
                DuplicateStatus = status,
                PossibleDuplicateOf = match.RelatedIds.Count > 0 ? [.. match.RelatedIds] : null,
            }));
        }
    }

    private sealed class CarIdResolvedAuthorComparer : IEqualityComparer<(int CarId, string Author)>
    {
        public static readonly CarIdResolvedAuthorComparer Instance = new();

        public bool Equals((int CarId, string Author) x, (int CarId, string Author) y) =>
            x.CarId == y.CarId && string.Equals(x.Author, y.Author, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int CarId, string Author) obj) =>
            HashCode.Combine(obj.CarId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Author));
    }

    private static LiveryCacheEntry RefreshStampsIfNeeded(LiveryCacheEntry existing, LiveryReader.LiveryFileHashes hashes)
    {
        if (existing.HeaderLength == hashes.HeaderLength
            && existing.HeaderLastWriteUtc == hashes.HeaderLastWriteUtc
            && existing.SourceThumbLength == hashes.SourceThumbLength
            && existing.SourceThumbLastWriteUtc == hashes.SourceThumbLastWriteUtc
            && existing.CLiveryLength == hashes.CLiveryLength
            && existing.CLiveryLastWriteUtc == hashes.CLiveryLastWriteUtc)
        {
            return existing;
        }

        return existing with
        {
            HeaderLength = hashes.HeaderLength,
            HeaderLastWriteUtc = hashes.HeaderLastWriteUtc,
            SourceThumbLength = hashes.SourceThumbLength,
            SourceThumbLastWriteUtc = hashes.SourceThumbLastWriteUtc,
            CLiveryLength = hashes.CLiveryLength,
            CLiveryLastWriteUtc = hashes.CLiveryLastWriteUtc,
        };
    }

    private bool CanReuseCacheByStamp(string folder, LiveryCacheEntry existing)
    {
        if (existing.SchemaVersion != LiveryCacheEntry.CurrentSchemaVersion) return false;
        if (existing.CLiveryHash is null) return false;

        string headerPath = Path.Combine(folder, "header");
        if (!LiveryReader.TryGetStamp(headerPath, out long headerLen, out DateTime headerMtime)) return false;
        if (headerLen != existing.HeaderLength || headerMtime != existing.HeaderLastWriteUtc) return false;

        string? thumbSource = ThumbnailService.FindSourceThumbnail(folder);
        if (thumbSource is null)
        {
            if (existing.ThumbnailFile is not null) return false;
        }
        else
        {
            if (existing.ThumbnailFile is null) return false;
            if (!LiveryReader.TryGetStamp(thumbSource, out long thumbLen, out DateTime thumbMtime)) return false;
            if (thumbLen != existing.SourceThumbLength || thumbMtime != existing.SourceThumbLastWriteUtc) return false;
            if (!File.Exists(Path.Combine(_appCacheService.ThumbsDir, existing.ThumbnailFile))) return false;
        }

        string cLiveryPath = Path.Combine(folder, "C_livery");
        if (!LiveryReader.TryGetStamp(cLiveryPath, out long cLiveryLen, out DateTime cLiveryMtime)) return false;
        if (cLiveryLen != existing.CLiveryLength || cLiveryMtime != existing.CLiveryLastWriteUtc) return false;

        return true;
    }

    private bool CanReuseCache(LiveryCacheEntry? existing, bool found, LiveryReader.LiveryFileHashes hashes)
    {
        if (!found || existing is null) return false;
        if (existing.SchemaVersion != LiveryCacheEntry.CurrentSchemaVersion) return false;

        bool thumbnailValid = hashes.SourceThumbnailPath is null
            ? existing.ThumbnailFile is null
            : existing.ThumbnailFile is not null
                && File.Exists(Path.Combine(_appCacheService.ThumbsDir, existing.ThumbnailFile));

        return !hashes.ThumbHashFailed
            && existing.HeaderHash == hashes.Header
            && existing.SourceThumbHash == hashes.SourceThumbnail
            && existing.CLiveryHash == hashes.CLivery
            && existing.CLiveryHash is not null
            && thumbnailValid;
    }
}
