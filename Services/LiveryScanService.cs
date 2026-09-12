using ForzaData;
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
    private readonly LiveryEntryFactory _entryFactory;

    public LiveryScanService(
        AppCacheService appCacheService,
        CarDatabaseService carDatabaseService,
        FavoriteService favoriteService,
        TagService tagService,
        AuthorCardService authorCardService)
    {
        _appCacheService = appCacheService;
        _entryFactory = new LiveryEntryFactory(appCacheService, carDatabaseService, favoriteService, tagService, authorCardService);
    }

    public Task<LiveryScanEntry> ScanAsync(string savePath, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Scan(savePath, progress, ct), ct);

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var (cache, _) = _appCacheService.Load();
            return _entryFactory.BuildEntries(cache.Values, ct);
        }, ct);

    private LiveryScanEntry Scan(string savePath, IProgress<string>? progress, CancellationToken ct)
    {
        var (oldCache, oldCacheLoadFailed) = _appCacheService.Load();

        var folders = LocalSaveService.GetListLiveryDirs(savePath)
            .Select(name => Path.Combine(savePath, name))
            .ToList();

        var newCache = new ConcurrentDictionary<string, LiveryCacheEntry>();
        var entries = new ConcurrentBag<LiveryEntry>();

        int reused = 0, parsed = 0, errors = 0, done = 0;
        int total = folders.Count;

        Parallel.ForEach(folders, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
        }, folder =>
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
                entries.Add(_entryFactory.ToEntry(cacheEntry));
            }
        });

        var finalCache = new Dictionary<string, LiveryCacheEntry>(newCache);

        int removed = oldCache.Keys.Except(finalCache.Keys).Count();

        var entriesList = entries.ToList();
        LiveryDuplicateService.MarkDuplicates(entriesList);

        bool cacheChanged = oldCache.Count != finalCache.Count
            || finalCache.Any(kv => !oldCache.TryGetValue(kv.Key, out var old) || !ReferenceEquals(old, kv.Value));

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
        string? SourceThumbnail,
        string? CLivery,
        byte[] HeaderData,
        byte[]? CLiveryBytes,
        string? SourceThumbnailPath,
        long HeaderLength,
        DateTime HeaderLastWriteUtc,
        long SourceThumbLength,
        DateTime SourceThumbLastWriteUtc,
        long CLiveryLength,
        DateTime CLiveryLastWriteUtc,
        bool ThumbHashFailed);

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

        var hashes = CalculateFileHashes(folder, existing);

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

    private LiveryFileHashes CalculateFileHashes(string folder, LiveryCacheEntry? existing)
    {
        string headerPath = Path.Combine(folder, "header");
        byte[] headerData = File.ReadAllBytes(headerPath);
        string headerHash = Convert.ToHexStringLower(SHA256.HashData(headerData));
        var headerInfo = new FileInfo(headerPath);

        string? thumbSource = ThumbnailService.FindSourceThumbnail(folder);
        string? sourceThumbHash = null;
        long thumbLength = 0;
        DateTime thumbLastWriteUtc = default;
        bool thumbHashFailed = false;
        if (thumbSource is not null)
        {
            try
            {
                var thumbInfo = new FileInfo(thumbSource);
                thumbLength = thumbInfo.Length;
                thumbLastWriteUtc = thumbInfo.LastWriteTimeUtc;
                if (existing?.SourceThumbHash is not null
                    && existing.SourceThumbLength == thumbLength
                    && existing.SourceThumbLastWriteUtc == thumbLastWriteUtc)
                {
                    sourceThumbHash = existing.SourceThumbHash;
                }
                else
                {
                    using var thumbStream = File.OpenRead(thumbSource);
                    sourceThumbHash = Convert.ToHexStringLower(SHA256.HashData(thumbStream));
                }
            }
            catch (Exception ex)
            {
                thumbHashFailed = true;
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
                if (existing?.CLiveryHash is not null
                    && existing.CLiveryLength == cLiveryLength
                    && existing.CLiveryLastWriteUtc == cLiveryLastWriteUtc)
                {
                    cLiveryHash = existing.CLiveryHash;
                }
                else if (existing?.CLiveryHash is null)
                {
                    cLiveryBytes = File.ReadAllBytes(cLiveryPath);
                    cLiveryHash = Convert.ToHexStringLower(SHA256.HashData(cLiveryBytes));
                }
                else
                {
                    using var cLiveryStream = File.OpenRead(cLiveryPath);
                    cLiveryHash = Convert.ToHexStringLower(SHA256.HashData(cLiveryStream));

                    if (cLiveryHash != existing.CLiveryHash)
                        cLiveryBytes = File.ReadAllBytes(cLiveryPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(cLiveryPath, $"Failed to read '{cLiveryPath}'", ex);
            }
        }
        else
        {
            AppLogger.LogErrorThrottled(cLiveryPath,
                $"'{cLiveryPath}' disappeared between folder listing and processing",
                new FileNotFoundException(null, cLiveryPath));
        }

        return new LiveryFileHashes(
            headerHash, sourceThumbHash, cLiveryHash, headerData, cLiveryBytes, thumbSource,
            headerInfo.Length, headerInfo.LastWriteTimeUtc,
            thumbLength, thumbLastWriteUtc,
            cLiveryLength, cLiveryLastWriteUtc, thumbHashFailed);
    }

    private bool CanReuseCache(LiveryCacheEntry? existing, bool found, LiveryFileHashes hashes)
    {
        if (!found || existing is null) return false;

        bool thumbnailValid = hashes.SourceThumbnailPath is null
            ? existing.ThumbnailFile is null
            : existing.ThumbnailFile is not null
                && File.Exists(Path.Combine(_appCacheService.ThumbsDir, existing.ThumbnailFile));

        return !hashes.ThumbHashFailed
            && existing.HeaderHash == hashes.Header
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
            string hashSuffix = hashes.SourceThumbnail is { Length: >= 12 } hash
                ? hash[..12]
                : hashes.SourceThumbnail ?? Guid.NewGuid().ToString("N")[..12];
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
