using ForzaData;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Globalization;
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
            IsFavorite = _favoriteService.IsFavorite(c.FolderName)
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
