using ForzaData;
using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.ViewModels;
using System.Collections.Concurrent;
using System.Runtime;

namespace LiveryGallery.Services;

internal sealed class LiveryScanner
{
    private readonly AppCacheService _appCacheService;
    private readonly AuthorCardService _authorCardService;
    private readonly LiveryArchiveService _archiveService;
    private readonly SaveFolderLock _saveFolderLock;
    private readonly LiveryEntryFactory _entryFactory;
    private readonly LiveryReader _reader;
    private static readonly string _duplicateInvalidationSignaturePath =
        Path.Combine(AppSettings.BaseCachePath, "duplicate_invalidation_signature.txt");
    private string? _lastDuplicateInvalidationSignature;
    private DateTime _lastLohCompaction = DateTime.MinValue;

    public LiveryScanner(
        AppCacheService appCacheService,
        CarDatabaseService carDatabaseService,
        FavoriteService favoriteService,
        TagService tagService,
        AuthorCardService authorCardService,
        LiveryArchiveService archiveService,
        SaveFolderLock saveFolderLock,
        LiveryIdService liveryIdService)
    {
        _appCacheService = appCacheService;
        _authorCardService = authorCardService;
        _archiveService = archiveService;
        _saveFolderLock = saveFolderLock;
        _entryFactory = new LiveryEntryFactory(appCacheService, carDatabaseService, favoriteService, tagService, authorCardService, liveryIdService);
        _reader = new LiveryReader();
        _lastDuplicateInvalidationSignature = TryLoadLastDuplicateInvalidationSignature();
    }

    private static string? TryLoadLastDuplicateInvalidationSignature()
    {
        try
        {
            return File.Exists(_duplicateInvalidationSignaturePath)
                ? File.ReadAllText(_duplicateInvalidationSignaturePath).Trim()
                : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_duplicateInvalidationSignaturePath, $"Failed to read '{_duplicateInvalidationSignaturePath}'", ex);
            return null;
        }
    }

    private static bool SaveLastDuplicateInvalidationSignature(string signature)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.BaseCachePath);
            AtomicFile.WriteAllText(_duplicateInvalidationSignaturePath, signature);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_duplicateInvalidationSignaturePath, $"Failed to write '{_duplicateInvalidationSignaturePath}'", ex);
            return false;
        }
    }

    public async Task<LiveryScanEntry> ScanAsync(
        string savePath, ulong? currentUserId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var _ = await _saveFolderLock.AcquireAsync(ct);
        return await Task.Run(() => Scan(savePath, currentUserId, progress, ct), ct);
    }

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(ulong? currentUserId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var (cache, _) = _appCacheService.Load();
            return _entryFactory.BuildEntries(cache.Values, currentUserId, ct);
        }, ct);

    private static bool IsFolderListStable(string savePath, List<string> firstListing, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(2))) ct.ThrowIfCancellationRequested();
        try
        {
            var first = firstListing.Select(Path.GetFileName).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var second = LocalSaveService.GetListLiveryDirs(savePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return first.SetEquals(second);
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(savePath + "|recheck", "Repeat folder listing failed", ex);
            return false;
        }
    }

    private LiveryScanEntry Scan(string savePath, ulong? currentUserId, IProgress<string>? progress, CancellationToken ct)
    {
        var (oldCache, oldCacheLoadFailed) = _appCacheService.Load();

        var folders = LocalSaveService.GetListLiveryDirs(savePath)
            .Select(name => Path.Combine(savePath, name))
            .ToList();

        var newCache = new ConcurrentDictionary<string, LiveryCacheEntry>();
        var freshlyParsedNames = new ConcurrentDictionary<string, byte>();
        var freshSectionCounts = new ConcurrentDictionary<string, uint[]?>();

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
                cacheEntry = TryReuseOrParse(folder, oldCache, progress, myDone, total, out bool wasReused, out uint[]? sectionCounts);
                if (wasReused)
                {
                    Interlocked.Increment(ref reused);
                }
                else
                {
                    Interlocked.Increment(ref parsed);
                    freshlyParsedNames[folderKey] = 0;
                    if (sectionCounts is not null) freshSectionCounts[folderKey] = sectionCounts;
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
        int removed = oldCache.Keys.Except(finalCache.Keys).Count();
        bool suspiciousDrop = oldCache.Count > 0 && removed > oldCache.Count * 0.2;
        bool snapshotUnreliable = false;
        if (errors == 0 && suspiciousDrop)
        {
            snapshotUnreliable = !IsFolderListStable(savePath, folders, ct);
            if (!snapshotUnreliable)
            {
                AppLogger.LogErrorThrottled(savePath + "|confirmed-drop",
                    $"Large drop in liveries confirmed by a repeat listing: {folders.Count} folders found for " +
                    $"{oldCache.Count} previously known ({removed} gone). Accepting it as the real state of the " +
                    "save folder and updating the cache.",
                    new InvalidOperationException("Diagnostic: confirmed livery count drop"));
            }
        }
        if (snapshotUnreliable)
        {
            AppLogger.LogErrorThrottled(savePath,
                $"Scan snapshot looks unreliable — only {folders.Count} folders found for {oldCache.Count} " +
                "previously known liveries, with 0 scan errors, and the folder list changed on a repeat listing. " +
                "Ignoring this scan result entirely (not saving, not applying) instead of wiping the persisted " +
                "cache — likely a transient container/path issue.",
                new InvalidOperationException("Unreliable scan snapshot"));
            finalCache = new Dictionary<string, LiveryCacheEntry>(oldCache);
            removed = 0;
            suspiciousDrop = false;
        }

        _entryFactory.ReconcileAuthorCards(finalCache.Values);
        int dirtyGroupsRecomputed = UpdateDuplicateStatuses(finalCache, oldCache, [.. freshlyParsedNames.Keys], freshSectionCounts, savePath);
        _entryFactory.EnsureLiveryIds(finalCache.Values);

        var entries = new ConcurrentBag<LiveryEntry>();
        Parallel.ForEach(finalCache.Values, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
        }, cacheEntry =>
        {
            entries.Add(_entryFactory.ToEntry(cacheEntry, currentUserId));
        });

        var entriesList = entries.ToList();
        bool cacheChanged = !snapshotUnreliable && (oldCache.Count != finalCache.Count
            || finalCache.Any(kv => !oldCache.TryGetValue(kv.Key, out var old) || old != kv.Value));

        if (cacheChanged)
        {
            progress?.Report(Strings.LoadingSavingCache);
            _appCacheService.Save(finalCache);
        }

        if (errors == 0 && !suspiciousDrop && !oldCacheLoadFailed && !snapshotUnreliable)
        {
            CleanupOrphanedThumbnails(finalCache);
        }
        else
        {
            AppLogger.LogErrorThrottled(savePath,
                $"Skipped orphaned thumbnail cleanup: {errors} scan errors, {removed} of {oldCache.Count} previously known liveries missing, cache load failed: {oldCacheLoadFailed}",
                new InvalidOperationException("Unreliable scan snapshot"));
        }

        if (dirtyGroupsRecomputed > 0 && DateTime.UtcNow - _lastLohCompaction > TimeSpan.FromMinutes(5))
        {
            _lastLohCompaction = DateTime.UtcNow;
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
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

            referenced.UnionWith(_archiveService.GetReferencedThumbnailFiles());

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
        out bool wasReused,
        out uint[]? cLiverySectionCounts)
    {
        cLiverySectionCounts = null;

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

        var parsed = _reader.ParseLivery(folder, hashes, existing, out cLiverySectionCounts);
        return parsed with { ThumbnailFile = GenerateThumbnailIfNeeded(folder, hashes, existing) };
    }

    private string? GenerateThumbnailIfNeeded(string folder, LiveryReader.LiveryFileHashes hashes, LiveryCacheEntry? previous)
    {
        if (hashes.SourceThumbnailPath is null) return null;

        string folderName = Path.GetFileName(folder);
        string candidate = ThumbnailService.ComputeDestinationFileName(folderName, folder, hashes.SourceThumbnail);
        string destPath = Path.Combine(_appCacheService.ThumbsDir, candidate);
        if (File.Exists(destPath)) return candidate;

        if (ThumbnailService.GenerateAndSave(hashes.SourceThumbnailPath, destPath))
            return candidate;

        if (previous?.ThumbnailFile is not null
            && File.Exists(Path.Combine(_appCacheService.ThumbsDir, previous.ThumbnailFile)))
        {
            return previous.ThumbnailFile;
        }

        return null;
    }

    private int UpdateDuplicateStatuses(
        Dictionary<string, LiveryCacheEntry> finalCache,
        Dictionary<string, LiveryCacheEntry> oldCache,
        HashSet<string> freshlyParsedNames,
        IReadOnlyDictionary<string, uint[]?> freshSectionCounts,
        string savePath)
    {
        int dirtyGroupsRecomputed = 0;
        int groupRecomputeFailures = 0;
        string ResolvedAuthor(LiveryCacheEntry e) => _authorCardService.ResolveDisplayName(e.Author, e.AuthorIdentityTagHex);
        string currentSignature = $"{LiveryDuplicateDetector.AlgorithmVersion}|{_authorCardService.GetIdentityMappingFingerprint()}";
        bool invalidationSignatureChanged = currentSignature != _lastDuplicateInvalidationSignature;
        var groups = finalCache.Values
            .GroupBy(e => (e.CarId, Author: ResolvedAuthor(e)), CarIdResolvedAuthorComparer.Instance)
            .ToList();

        var updates = new ConcurrentBag<(string Name, LiveryCacheEntry Entry)>();

        Parallel.ForEach(groups, new ParallelOptions
        {
            MaxDegreeOfParallelism = 2,
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

            if (!membershipChanged && !anyMemberFresh && !invalidationSignatureChanged)
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

            Interlocked.Increment(ref dirtyGroupsRecomputed);
            try
            {
                RecomputeDuplicatesForDirtyGroup(members, group.Key.Author, updates, freshSectionCounts, savePath);
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(
                    $"{group.Key.CarId}|{group.Key.Author}",
                    $"Failed to recompute duplicates for group (CarId={group.Key.CarId}, Author='{group.Key.Author}', {members.Count} members)",
                    ex);
                Interlocked.Increment(ref groupRecomputeFailures);
                foreach (var member in members)
                {
                    if (oldCache.TryGetValue(member.FolderName, out var old))
                        updates.Add((member.FolderName, member with
                        {
                            DuplicateStatus = old.DuplicateStatus,
                            PossibleDuplicateOf = old.PossibleDuplicateOf,
                        }));
                }
            }
        });

        foreach (var (name, entry) in updates)
            finalCache[name] = entry;

        if (invalidationSignatureChanged && groupRecomputeFailures == 0)
        {
            if (SaveLastDuplicateInvalidationSignature(currentSignature))
                _lastDuplicateInvalidationSignature = currentSignature;
        }

        return dirtyGroupsRecomputed;
    }

    private static void RecomputeDuplicatesForDirtyGroup(
        List<LiveryCacheEntry> members,
        string resolvedAuthor,
        ConcurrentBag<(string Name, LiveryCacheEntry Entry)> updates,
        IReadOnlyDictionary<string, uint[]?> freshSectionCounts,
        string savePath)
    {
        var candidates = new List<LiveryDuplicateCandidate>(members.Count);
        foreach (var member in members)
        {
            uint[]? sectionCounts = freshSectionCounts.TryGetValue(member.FolderName, out var cached)
                ? cached
                : LiveryReader.TryReadCLiverySectionCounts(Path.Combine(savePath, member.FolderName));

            var shapes = LiveryReader.TryReadCLiveryShapeFingerprints(Path.Combine(savePath, member.FolderName));
            candidates.Add(new LiveryDuplicateCandidate(member.FolderName, member.CarId, resolvedAuthor, member.CLiveryHash, sectionCounts, shapes));
        }

        var results = LiveryDuplicateDetector.Detect(candidates);

        foreach (var member in members)
        {
            DuplicateStatus status = DuplicateStatus.Ok;
            IReadOnlyList<DuplicateRelation>? possibleDuplicateOf = null;

            if (results.TryGetValue(member.FolderName, out var match))
            {
                status = match.Status switch
                {
                    LiveryDuplicateStatus.Duplicate => DuplicateStatus.Duplicate,
                    LiveryDuplicateStatus.FullDuplicate => DuplicateStatus.Duplicate,
                    LiveryDuplicateStatus.PossibleDuplicate => DuplicateStatus.PossibleDuplicate,
                    _ => DuplicateStatus.Ok,
                };

                possibleDuplicateOf = match.RelatedIds.Count > 0
                    ? [.. match.RelatedIds.Select(r => new DuplicateRelation(r.Id, r.Score))]
                    : null;
            }

            updates.Add((member.FolderName, member with
            {
                DuplicateStatus = status,
                PossibleDuplicateOf = possibleDuplicateOf,
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
        if (existing.GenerationAlgorithmVersion != LiveryGenerationDetector.AlgorithmVersion) return false;
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

        return false;
    }

    private bool CanReuseCache(LiveryCacheEntry? existing, bool found, LiveryReader.LiveryFileHashes hashes)
    {
        if (!found || existing is null) return false;
        if (existing.SchemaVersion != LiveryCacheEntry.CurrentSchemaVersion) return false;
        if (existing.GenerationAlgorithmVersion != LiveryGenerationDetector.AlgorithmVersion) return false;

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
