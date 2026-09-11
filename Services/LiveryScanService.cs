using ForzaData;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services;

internal partial class LiveryScanService
{
    private static readonly Regex FolderNameRegex =
        new(@"Livery_(?<carId>\d+)_(?<ts>\d+)", RegexOptions.Compiled);

    private readonly AppCacheService _appCacheService;
    private readonly CarDatabaseService _carDatabaseService;
    private readonly FavoriteService _favoriteService;
    private readonly TagService _tagService;

    public LiveryScanService(
        AppCacheService appCacheService,
        CarDatabaseService carDatabaseService,
        FavoriteService favoriteService,
        TagService tagService)
    {
        _appCacheService = appCacheService;
        _carDatabaseService = carDatabaseService;
        _favoriteService = favoriteService;
        _tagService = tagService;
    }

    public Task<LiveryScanEntry> ScanAsync(string savePath, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Scan(savePath, progress, ct), ct);

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var cache = _appCacheService.Load();
            var entries = new List<LiveryEntry>(cache.Count);
            foreach (var cacheEntry in cache.Values)
            {
                ct.ThrowIfCancellationRequested();
                entries.Add(ToEntry(cacheEntry));
            }
            MarkDuplicates(entries);
            return entries;
        }, ct);

    private LiveryScanEntry Scan(string savePath, IProgress<string>? progress, CancellationToken ct)
    {
        var oldCache = _appCacheService.Load();

        var folders = LocalSaveService.GetListLiveryDirs(savePath)
            .Select(name => Path.Combine(savePath, name))
            .ToList();

        var newCache = new ConcurrentDictionary<string, LiveryCacheEntry>();
        var entries = new ConcurrentBag<LiveryEntry>();

        int reused = 0, parsed = 0, errors = 0, done = 0;
        int total = folders.Count;

        Parallel.ForEach(folders, new ParallelOptions { CancellationToken = ct }, folder =>
        {
            int myDone = Interlocked.Increment(ref done);

            LiveryCacheEntry? cacheEntry = null;
            try
            {
                cacheEntry = TryReuseOrParse(folder, oldCache, progress, myDone, total, out bool wasReused);
                if (wasReused) Interlocked.Increment(ref reused); else Interlocked.Increment(ref parsed);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                AppLogger.LogErrorThrottled(folder, $"Scan error: {Path.GetFileName(folder)}", ex);
                if (oldCache.TryGetValue(folder, out var fallback))
                    cacheEntry = fallback;
            }

            if (cacheEntry is not null)
            {
                newCache[folder] = cacheEntry;
                entries.Add(ToEntry(cacheEntry));
            }
        });

        var finalCache = new Dictionary<string, LiveryCacheEntry>(newCache);

        int removed = oldCache.Keys.Except(finalCache.Keys).Count();

        var entriesList = entries.ToList();
        MarkDuplicates(entriesList);

        bool cacheChanged = oldCache.Count != finalCache.Count
            || finalCache.Any(kv => !oldCache.TryGetValue(kv.Key, out var old) || !ReferenceEquals(old, kv.Value));

        if (cacheChanged)
        {
            progress?.Report(Strings.LoadingSavingCache);
            _appCacheService.Save(finalCache);
        }

        CleanupOrphanedThumbnails(finalCache);

        return new LiveryScanEntry
        {
            Entries = entriesList,
            ReusedFromCache = reused,
            Parsed = parsed,
            Errors = errors,
            Removed = removed
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

    private sealed record LiveryFileHashes(
        string Header,
        string SourceThumbnail,
        string? CLivery,
        byte[] HeaderData,
        byte[]? CLiveryBytes,
        string? SourceThumbnailPath,
        long HeaderLength,
        DateTime HeaderLastWriteUtc,
        long SourceThumbLength,
        DateTime SourceThumbLastWriteUtc,
        long CLiveryLength,
        DateTime CLiveryLastWriteUtc);

    private LiveryCacheEntry TryReuseOrParse(
        string folder,
        Dictionary<string, LiveryCacheEntry> oldCache,
        IProgress<string>? progress,
        int done,
        int total,
        out bool wasReused)
    {
        bool found = oldCache.TryGetValue(folder, out var existing);
        if (found && CanReuseCacheByStamp(folder, existing!))
        {
            wasReused = true;
            return existing!;
        }

        var hashes = CalculateFileHashes(folder, existing?.CLiveryHash);

        if (CanReuseCache(existing, found, hashes))
        {
            wasReused = true;
            return RefreshStampsIfNeeded(existing!, hashes);
        }

        wasReused = false;
        progress?.Report(string.Format(Strings.ParsingProgress, done, total, Path.GetFileName(folder)));
        return ParseLivery(folder, hashes, existing);
    }

    private static LiveryCacheEntry RefreshStampsIfNeeded(LiveryCacheEntry existing, LiveryFileHashes hashes)
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
        if (existing.CLiveryHash is null || existing.SectionCounts is null) return false;

        string headerPath = Path.Combine(folder, "header");
        if (!TryGetStamp(headerPath, out long headerLen, out DateTime headerMtime)) return false;
        if (headerLen != existing.HeaderLength || headerMtime != existing.HeaderLastWriteUtc) return false;

        string? thumbSource = ThumbnailService.FindSourceThumbnail(folder);
        if (thumbSource is null)
        {
            if (existing.ThumbnailFile is not null) return false;
        }
        else
        {
            if (existing.ThumbnailFile is null) return false;
            if (!TryGetStamp(thumbSource, out long thumbLen, out DateTime thumbMtime)) return false;
            if (thumbLen != existing.SourceThumbLength || thumbMtime != existing.SourceThumbLastWriteUtc) return false;
            if (!File.Exists(Path.Combine(_appCacheService.ThumbsDir, existing.ThumbnailFile))) return false;
        }

        string cLiveryPath = Path.Combine(folder, "C_livery");
        if (!TryGetStamp(cLiveryPath, out long cLiveryLen, out DateTime cLiveryMtime)) return false;
        if (cLiveryLen != existing.CLiveryLength || cLiveryMtime != existing.CLiveryLastWriteUtc) return false;

        return true;
    }

    private static bool TryGetStamp(string path, out long length, out DateTime lastWriteUtc)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { length = 0; lastWriteUtc = default; return false; }
            length = info.Length;
            lastWriteUtc = info.LastWriteTimeUtc;
            return true;
        }
        catch
        {
            length = 0;
            lastWriteUtc = default;
            return false;
        }
    }

    private LiveryFileHashes CalculateFileHashes(string folder, string? oldCLiveryHash)
    {
        string headerPath = Path.Combine(folder, "header");
        byte[] headerData = File.ReadAllBytes(headerPath);
        string headerHash = Convert.ToHexStringLower(SHA256.HashData(headerData));
        var headerInfo = new FileInfo(headerPath);

        string? thumbSource = ThumbnailService.FindSourceThumbnail(folder);
        string sourceThumbHash = "";
        long thumbLength = 0;
        DateTime thumbLastWriteUtc = default;
        if (thumbSource is not null)
        {
            try
            {
                using var thumbStream = File.OpenRead(thumbSource);
                sourceThumbHash = Convert.ToHexStringLower(SHA256.HashData(thumbStream));
                var thumbInfo = new FileInfo(thumbSource);
                thumbLength = thumbInfo.Length;
                thumbLastWriteUtc = thumbInfo.LastWriteTimeUtc;
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(thumbSource, $"Failed to read preview '{thumbSource}'", ex);
            }
        }

        string cLiveryPath = Path.Combine(folder, "C_livery");
        byte[]? cLiveryBytes = null;
        string? cLiveryHash = null;
        long cLiveryLength = 0;
        DateTime cLiveryLastWriteUtc = default;
        if (File.Exists(cLiveryPath))
        {
            try
            {
                var cLiveryInfo = new FileInfo(cLiveryPath);
                cLiveryLength = cLiveryInfo.Length;
                cLiveryLastWriteUtc = cLiveryInfo.LastWriteTimeUtc;
                using var cLiveryStream = File.OpenRead(cLiveryPath);
                cLiveryHash = Convert.ToHexStringLower(SHA256.HashData(cLiveryStream));

                if (cLiveryHash != oldCLiveryHash)
                {
                    cLiveryStream.Position = 0;
                    using var ms = new MemoryStream(checked((int)cLiveryLength));
                    cLiveryStream.CopyTo(ms);
                    cLiveryBytes = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(cLiveryPath, $"Failed to read '{cLiveryPath}'", ex);
            }
        }

        return new LiveryFileHashes(
            headerHash, sourceThumbHash, cLiveryHash, headerData, cLiveryBytes, thumbSource,
            headerInfo.Length, headerInfo.LastWriteTimeUtc,
            thumbLength, thumbLastWriteUtc,
            cLiveryLength, cLiveryLastWriteUtc);
    }

    private bool CanReuseCache(LiveryCacheEntry? existing, bool found, LiveryFileHashes hashes)
    {
        if (!found || existing is null) return false;

        bool thumbnailValid = hashes.SourceThumbnailPath is null
            ? existing.ThumbnailFile is null
            : existing.ThumbnailFile is not null
                && File.Exists(Path.Combine(_appCacheService.ThumbsDir, existing.ThumbnailFile));

        return existing.HeaderHash == hashes.Header
            && existing.SourceThumbHash == hashes.SourceThumbnail
            && existing.CLiveryHash == hashes.CLivery
            && existing.CLiveryHash is not null
            && existing.SectionCounts is not null
            && thumbnailValid;
    }

    private LiveryCacheEntry ParseLivery(string folder, LiveryFileHashes hashes, LiveryCacheEntry? previous)
    {
        string folderName = Path.GetFileName(folder);
        var (carId, tsRaw) = ParseFolderName(folderName);

        var (_, parsedHeader) = NativeHeaderParser.TryParseHeader(hashes.HeaderData);
        byte[]? cLiveryBytes = hashes.CLiveryBytes;
        if (cLiveryBytes is null && hashes.CLivery is not null)
        {
            try
            {
                cLiveryBytes = File.ReadAllBytes(Path.Combine(folder, "C_livery"));
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(folder, $"Failed to re-read C_livery in '{folder}'", ex);
            }
        }

        uint[]? sectionCounts = null;
        if (cLiveryBytes is not null)
        {
            try
            {
                var (liveryResult, livery) = NativeHeaderParser.TryParseCLivery(cLiveryBytes);
                if (liveryResult == LiveryParseResult.Ok && livery is not null)
                    sectionCounts = [.. livery.SectionCounts];
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(folder, $"Failed to parse C_livery in '{folder}'", ex);
            }
        }

        string liveryName = !string.IsNullOrWhiteSpace(parsedHeader?.LiveryName) ? parsedHeader!.LiveryName : Strings.LiveryNoName;
        string author = !string.IsNullOrWhiteSpace(parsedHeader?.CreatorName) ? parsedHeader!.CreatorName : Strings.UnknownAuthor;
        int year = parsedHeader is { Year: > 0 } ? parsedHeader.Year : 0;
        int month = parsedHeader is { Month: >= 1 and <= 12 } ? parsedHeader.Month : 0;

        DateTime? downloadDate = tsRaw is not null ? TryDecodeTimestamp(tsRaw) : null;

        string? thumbnailFile = null;
        if (hashes.SourceThumbnailPath is not null)
        {
            string hashSuffix = hashes.SourceThumbnail.Length >= 12 ? hashes.SourceThumbnail[..12] : hashes.SourceThumbnail;
            string candidate = SanitiseFileName(folderName) + "_" + StableHash(folder) + "_" + hashSuffix + ".png";
            string destPath = Path.Combine(_appCacheService.ThumbsDir, candidate);
            if (ThumbnailService.GenerateAndSave(hashes.SourceThumbnailPath, destPath))
            {
                thumbnailFile = candidate;
            }
            else if (previous?.ThumbnailFile is not null
                && File.Exists(Path.Combine(_appCacheService.ThumbsDir, previous.ThumbnailFile)))
            {
                thumbnailFile = previous.ThumbnailFile;
            }
        }

        return new LiveryCacheEntry
        {
            FolderPath = folder,
            FolderName = folderName,
            LiveryName = liveryName,
            Author = author,
            CarId = carId,
            CreatedYear = year,
            CreatedMonth = month,
            DownloadDate = downloadDate,
            ThumbnailFile = thumbnailFile,
            HeaderHash = hashes.Header,
            SourceThumbHash = hashes.SourceThumbnail,
            CLiveryHash = hashes.CLivery,
            HeaderLength = hashes.HeaderLength,
            HeaderLastWriteUtc = hashes.HeaderLastWriteUtc,
            SourceThumbLength = hashes.SourceThumbLength,
            SourceThumbLastWriteUtc = hashes.SourceThumbLastWriteUtc,
            CLiveryLength = hashes.CLiveryLength,
            CLiveryLastWriteUtc = hashes.CLiveryLastWriteUtc,
            SectionCounts = sectionCounts,
        };
    }

    private LiveryEntry ToEntry(LiveryCacheEntry c)
    {
        var car = _carDatabaseService.Get(c.CarId);
        return new LiveryEntry
        {
            FolderPath = c.FolderPath,
            FolderName = c.FolderName,
            LiveryName = c.LiveryName,
            Author = c.Author,
            CarId = c.CarId,
            CarManufacturerRaw = car?.Manufacturer ?? string.Empty,
            CarModelNameRaw = car?.Name ?? string.Empty,
            CarYear = car?.Year,
            CarKnown = car is not null,
            CreatedYear = c.CreatedYear,
            CreatedMonth = c.CreatedMonth,
            DownloadDate = c.DownloadDate,
            ThumbnailPath = c.ThumbnailFile is not null 
                ? Path.Combine(_appCacheService.ThumbsDir, c.ThumbnailFile) 
                : null,
            HasThumbnail = c.ThumbnailFile is not null,
            Tags = _tagService.GetTags(c.FolderName),
            IsFavorite = _favoriteService.IsFavorite(c.FolderName),
            CLiveryHash = c.CLiveryHash,
            SectionCounts = c.SectionCounts
        };
    }

    private static void MarkDuplicates(List<LiveryEntry> entries)
    {
        const int sectionCount = 11;
        var exactGroups = entries
            .Where(e => e.CLiveryHash is not null)
            .GroupBy(e => (e.CarId, e.Author, e.CLiveryHash), CarIdAuthorHashComparer.Instance);

        foreach (var group in exactGroups)
        {
            if (group.Count() <= 1) continue;
            foreach (var entry in group)
                entry.DuplicateStatus = DuplicateStatus.Duplicate;
        }

        var candidateGroups = entries
            .Where(e => e.DuplicateStatus != DuplicateStatus.Duplicate && e.SectionCounts is { Count: sectionCount })
            .GroupBy(e => (e.CarId, e.Author), CarIdAuthorComparer.Instance);

        foreach (var group in candidateGroups)
            MarkPossibleDuplicatesInGroup([.. group]);
    }

    private sealed class CarIdAuthorHashComparer : IEqualityComparer<(int CarId, string Author, string? CLiveryHash)>
    {
        public static readonly CarIdAuthorHashComparer Instance = new();

        public bool Equals((int CarId, string Author, string? CLiveryHash) x, (int CarId, string Author, string? CLiveryHash) y) =>
            x.CarId == y.CarId
            && string.Equals(x.Author, y.Author, StringComparison.OrdinalIgnoreCase)
            && x.CLiveryHash == y.CLiveryHash;

        public int GetHashCode((int CarId, string Author, string? CLiveryHash) obj) =>
            HashCode.Combine(obj.CarId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Author), obj.CLiveryHash);
    }

    private sealed class CarIdAuthorComparer : IEqualityComparer<(int CarId, string Author)>
    {
        public static readonly CarIdAuthorComparer Instance = new();

        public bool Equals((int CarId, string Author) x, (int CarId, string Author) y) =>
            x.CarId == y.CarId && string.Equals(x.Author, y.Author, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int CarId, string Author) obj) =>
            HashCode.Combine(obj.CarId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Author));
    }

    private static void MarkPossibleDuplicatesInGroup(List<LiveryEntry> items)
    {
        const int minSharedForCandidate = 2;

        var index = new Dictionary<(int Position, uint Value), List<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            var counts = items[i].SectionCounts!;
            for (int pos = 0; pos < counts.Count; pos++)
            {
                if (counts[pos] == 0) continue;
                var key = (pos, counts[pos]);
                if (!index.TryGetValue(key, out var list))
                    index[key] = list = [];
                list.Add(i);
            }
        }

        for (int i = 0; i < items.Count; i++)
        {
            var counts = items[i].SectionCounts!;
            Dictionary<int, int>? sharedCounts = null;

            for (int pos = 0; pos < counts.Count; pos++)
            {
                if (counts[pos] == 0) continue;
                if (!index.TryGetValue((pos, counts[pos]), out var candidates)) continue;

                foreach (int j in candidates)
                {
                    if (j <= i) continue;
                    sharedCounts ??= [];
                    sharedCounts[j] = sharedCounts.GetValueOrDefault(j) + 1;
                }
            }

            if (sharedCounts is null) continue;

            foreach (var (j, shared) in sharedCounts)
            {
                if (shared < minSharedForCandidate) continue;
                if (AreSectionsSimilar(items[i].SectionCounts!, items[j].SectionCounts!))
                {
                    items[i].DuplicateStatus = DuplicateStatus.PossibleDuplicate;
                    items[j].DuplicateStatus = DuplicateStatus.PossibleDuplicate;
                }
            }
        }
    }

    private const double MinSectionMatchRatio = 0.6;
    private const int MinRelevantSections = 3;

    private static bool AreSectionsSimilar(IReadOnlyList<uint> a, IReadOnlyList<uint> b)
    {
        int relevant = 0, matches = 0;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] == 0 && b[i] == 0) continue;
            relevant++;
            if (a[i] == b[i]) matches++;
        }

        if (relevant < MinRelevantSections) return false;
        return (double)matches / relevant >= MinSectionMatchRatio;
    }

    private static (int CarId, string? Timestamp) ParseFolderName(string folderName)
    {
        var m = FolderNameRegex.Match(folderName);
        if (!m.Success) return (0, null);

        int carId = int.TryParse(m.Groups["carId"].Value, out var id) ? id : 0;
        string? ts = m.Groups["ts"].Success ? m.Groups["ts"].Value : null;
        return (carId, ts);
    }

    private static DateTime? TryDecodeTimestamp(string raw)
    {
        if (raw.Length != 14) return null;
        if (!DateTime.TryParseExact(raw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return null;

        DateTime minPlausible = new(2015, 1, 1);
        DateTime maxPlausible = DateTime.UtcNow.AddDays(1);
        return d >= minPlausible && d <= maxPlausible ? d : null;
    }

    private static string SanitiseFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static string StableHash(string input)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 1099511628211;
            }
            return hash.ToString("x16");
        }
    }
}
