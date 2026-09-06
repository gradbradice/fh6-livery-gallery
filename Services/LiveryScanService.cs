using ForzaData;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
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

    private LiveryScanEntry Scan(string savePath, IProgress<string>? progress, CancellationToken ct)
    {
        var oldCache = _appCacheService.Load();

        var folders = LocalSaveService.GetListLiveryDirs(savePath)
            .Select(name => Path.Combine(savePath, name))
            .ToList();

        var newCache = new Dictionary<string, LiveryCacheEntry>(folders.Count);
        var entries = new List<LiveryEntry>(folders.Count);

        int reused = 0, parsed = 0, errors = 0;
        int total = folders.Count;
        int done = 0;

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            done++;

            LiveryCacheEntry? cacheEntry = null;
            try
            {
                cacheEntry = TryReuseOrParse(folder, oldCache, progress, done, total, out bool wasReused);
                if (wasReused) reused++; else parsed++;
            }
            catch
            {
                errors++;
            }

            if (cacheEntry is not null)
            {
                newCache[folder] = cacheEntry;
                entries.Add(ToEntry(cacheEntry));
            }
        }

        int removed = oldCache.Keys.Except(newCache.Keys).Count();

        MarkDuplicates(entries);

        progress?.Report(Strings.LoadingSavingCache);
        _appCacheService.Save(newCache);

        return new LiveryScanEntry
        {
            Entries = entries,
            ReusedFromCache = reused,
            Parsed = parsed,
            Errors = errors,
            Removed = removed
        };
    }

    private LiveryCacheEntry TryReuseOrParse(
        string folder,
        Dictionary<string, LiveryCacheEntry> oldCache,
        IProgress<string>? progress,
        int done,
        int total,
        out bool wasReused)
    {
        string headerPath = Path.Combine(folder, "header");
        DateTime headerWrite = File.GetLastWriteTimeUtc(headerPath);

        string? thumbSource = ThumbnailService.FindSourceThumbnail(folder);
        //DateTime thumbWrite = thumbSource is not null ? File.GetLastWriteTimeUtc(thumbSource) : DateTime.MinValue;

        if (oldCache.TryGetValue(folder, out var existing)
            && existing.FilesWruteUTC == headerWrite
            && existing.CLiveryHash is not null
            && existing.SectionCounts is not null
            && (existing.ThumbnailFile is null || File.Exists(Path.Combine(_appCacheService.ThumbsDir, existing.ThumbnailFile))))
        {
            wasReused = true;
            return existing;
        }

        wasReused = false;
        progress?.Report(string.Format(Strings.ParsingProgress, done, total, Path.GetFileName(folder)));

        string folderName = Path.GetFileName(folder);
        var (carId, tsRaw) = ParseFolderName(folderName);

        byte[] headerData = File.ReadAllBytes(headerPath);
        var (_, parsedHeader) = NativeHeaderParser.TryParseHeader(headerData);

        string? cLiveryHash = null;
        uint[]? sectionCounts = null;
        string cLiveryPath = Path.Combine(folder, "C_livery");
        if (File.Exists(cLiveryPath))
        {
            try
            {
                byte[] cLiveryBytes = File.ReadAllBytes(cLiveryPath);
                cLiveryHash = Convert.ToHexStringLower(SHA256.HashData(cLiveryBytes));
                var (liveryResult, livery) = NativeHeaderParser.TryParseCLivery(cLiveryBytes);
                if (liveryResult == LiveryParseResult.Ok && livery is not null)
                    sectionCounts = [.. livery.SectionCounts];
            }
            catch
            {

            }
        }

        string liveryName = !string.IsNullOrWhiteSpace(parsedHeader?.LiveryName) ? parsedHeader!.LiveryName : Strings.LiveryNoName;
        string author = !string.IsNullOrWhiteSpace(parsedHeader?.CreatorName) ? parsedHeader!.CreatorName : Strings.UnknownAuthor;
        int year = parsedHeader is { Year: > 0 } ? parsedHeader.Year : 0;
        int month = parsedHeader is { Month: >= 1 and <= 12 } ? parsedHeader.Month : 0;

        DateTime? downloadDate = tsRaw is not null ? TryDecodeTimestamp(tsRaw) : null;

        string? thumbnailFile = null;
        if (thumbSource is not null)
        {
            string candidate = SanitiseFileName(folderName) + "_" + StableHash(folder) + ".png";
            string destPath = Path.Combine(_appCacheService.ThumbsDir, candidate);
            if (ThumbnailService.GenerateAndSave(thumbSource, destPath))
                thumbnailFile = candidate;
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
            FilesWruteUTC = headerWrite,
            //SourceThumbWriteUtc = thumbWrite
            CLiveryHash = cLiveryHash,
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
            .GroupBy(e => (e.CarId, Author: e.Author.ToLowerInvariant(), e.CLiveryHash));

        foreach (var group in exactGroups)
        {
            if (group.Count() <= 1) continue;
            foreach (var entry in group)
                entry.DuplicateStatus = DuplicateStatus.Duplicate;
        }

        var candidateGroups = entries
            .Where(e => e.DuplicateStatus != DuplicateStatus.Duplicate && e.SectionCounts is { Count: sectionCount })
            .GroupBy(e => (e.CarId, Author: e.Author.ToLowerInvariant()));

        foreach (var group in candidateGroups)
        {
            var items = group.ToList();
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (AreSectionsSimilar(items[i].SectionCounts!, items[j].SectionCounts!))
                    {
                        items[i].DuplicateStatus = DuplicateStatus.PossibleDuplicate;
                        items[j].DuplicateStatus = DuplicateStatus.PossibleDuplicate;
                    }
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
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }
    }
}
