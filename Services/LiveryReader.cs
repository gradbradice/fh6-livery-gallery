using ForzaData;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services;

internal sealed partial class LiveryReader
{
    [GeneratedRegex(@"Livery_(?<carId>\d+)_(?<ts>\d+)")]
    private static partial Regex FolderNameRegex();
    private int _carIdMismatchLogged;


    internal sealed record LiveryFileHashes(
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

    internal static bool TryGetStamp(string path, out long length, out DateTime lastWriteUtc)
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

    public LiveryFileHashes CalculateFileHashes(string folder, LiveryCacheEntry? existing)
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
                cLiveryBytes = File.ReadAllBytes(cLiveryPath);
                cLiveryHash = Convert.ToHexStringLower(SHA256.HashData(cLiveryBytes));
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

    public LiveryCacheEntry ParseLivery(string folder, LiveryFileHashes hashes, LiveryCacheEntry? previous, out uint[]? cLiverySectionCounts)
    {
        string folderName = Path.GetFileName(folder);
        var (folderCarId, tsRaw) = ParseFolderName(folderName);
        var (_, parsedHeader) = NativeHeaderParser.TryParseHeader(hashes.HeaderData);
        uint? headerCarId = parsedHeader?.TargetCarId;

        byte[]? cLiveryBytes = hashes.CLiveryBytes;
        uint? cLiveryCarId;
        bool isPossiblyGenerated;
        bool hasNoLayers;
        bool hasParseError;
        cLiverySectionCounts = null;
        if (previous is not null && hashes.CLivery is not null && hashes.CLivery == previous.CLiveryHash
            && previous.GenerationAlgorithmVersion == LiveryGenerationDetector.AlgorithmVersion)
        {
            cLiveryCarId = previous.CLiveryCarId;
            isPossiblyGenerated = previous.IsPossiblyGenerated;
            hasNoLayers = previous.HasNoLayers;
            hasParseError = previous.HasParseError;
        }
        else
        {
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

            cLiveryCarId = null;
            isPossiblyGenerated = false;
            hasNoLayers = false;
            hasParseError = false;
            if (cLiveryBytes is not null)
            {
                try
                {
                    var (liveryResult, livery) = NativeHeaderParser.TryParseCLivery(cLiveryBytes);
                    if (liveryResult == LiveryParseResult.Ok && livery is not null)
                    {
                        cLiveryCarId = livery.TargetCarId;
                        cLiverySectionCounts = [.. livery.SectionCounts];
                    }
                    else
                    {
                        hasParseError = true;
                    }

                    var (_, verdict, _) = LiveryGenerationDetector.Detect(cLiveryBytes);
                    isPossiblyGenerated = verdict == LiveryGenerationVerdict.Generated;
                    hasNoLayers = verdict == LiveryGenerationVerdict.NoLayers;
                }
                catch (Exception ex)
                {
                    hasParseError = true;
                    AppLogger.LogErrorThrottled(folder, $"Failed to parse C_livery in '{folder}'", ex);
                }
            }
        }

        var (carId, carIdConsistency) = ResolveCarId(folderName, folderCarId, headerCarId, cLiveryCarId);

        string liveryName = !string.IsNullOrWhiteSpace(parsedHeader?.LiveryName) ? parsedHeader!.LiveryName : Strings.LiveryNoName;
        string author = !string.IsNullOrWhiteSpace(parsedHeader?.CreatorName) ? parsedHeader!.CreatorName : Strings.UnknownAuthor;
        string? authorIdentityTagHex = parsedHeader?.CreatorIdentityTag is { Length: 8 } tag ? Convert.ToHexStringLower(tag) : null;
        ulong? creatorUserId = parsedHeader?.CreatorIdentityTag is { Length: 8 } ? parsedHeader!.CreatorUserId : null;
        int year = parsedHeader is { Year: > 0 } ? parsedHeader.Year : 0;
        int month = parsedHeader is { Month: >= 1 and <= 12 } ? parsedHeader.Month : 0;

        DateTime? downloadDate = tsRaw is not null ? TryDecodeTimestamp(tsRaw) : null;

        return new LiveryCacheEntry
        {
            SchemaVersion = LiveryCacheEntry.CurrentSchemaVersion,
            FolderName = folderName,
            LiveryName = liveryName,
            Author = author,
            AuthorIdentityTagHex = authorIdentityTagHex,
            CreatorUserId = creatorUserId,
            IsPossiblyGenerated = isPossiblyGenerated,
            GenerationAlgorithmVersion = LiveryGenerationDetector.AlgorithmVersion,
            HasNoLayers = hasNoLayers,
            HasParseError = hasParseError,
            CLiveryCarId = cLiveryCarId,
            CarId = carId,
            CarIdConsistency = carIdConsistency,
            CreatedYear = year,
            CreatedMonth = month,
            DownloadDate = downloadDate,
            ThumbnailFile = null,
            HeaderHash = hashes.Header,
            SourceThumbHash = hashes.SourceThumbnail,
            CLiveryHash = hashes.CLivery,
            HeaderLength = hashes.HeaderLength,
            HeaderLastWriteUtc = hashes.HeaderLastWriteUtc,
            SourceThumbLength = hashes.SourceThumbLength,
            SourceThumbLastWriteUtc = hashes.SourceThumbLastWriteUtc,
            CLiveryLength = hashes.CLiveryLength,
            CLiveryLastWriteUtc = hashes.CLiveryLastWriteUtc,
        };
    }

    public static uint[]? TryReadCLiverySectionCounts(string folderPath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(folderPath, "C_livery"));
            var (result, livery) = NativeHeaderParser.TryParseCLivery(bytes);
            return result == LiveryParseResult.Ok && livery is not null ? [.. livery.SectionCounts] : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(folderPath, $"Failed to re-read C_livery for duplicate check: '{folderPath}'", ex);
            return null;
        }
    }

    public static IReadOnlyList<LiveryShapeFingerprint>? TryReadCLiveryShapeFingerprints(string folderPath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(folderPath, "C_livery"));
            var (result, shapes) = NativeHeaderParser.TryExtractShapeFingerprints(bytes);
            return result == LiveryParseResult.Ok ? shapes : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(folderPath, $"Failed to re-read C_livery for full-duplicate check: '{folderPath}'", ex);
            return null;
        }
    }

    private (int CarId, LiveryConsistency Consistency) ResolveCarId(
        string folderName, int folderCarId, uint? headerCarId, uint? cLiveryCarId)
    {
        var (resolved, consistency) = CarIdResolver.Resolve(folderCarId, headerCarId, cLiveryCarId);

        if (consistency == LiveryConsistency.Mismatched && Interlocked.CompareExchange(ref _carIdMismatchLogged, 1, 0) == 0)
        {
            AppLogger.LogError(
                $"CarId mismatch between sources for '{folderName}': folder={folderCarId}, " +
                $"header={(headerCarId.HasValue ? headerCarId.Value.ToString() : "n/a")}, " +
                $"C_livery={(cLiveryCarId.HasValue ? cLiveryCarId.Value.ToString() : "n/a")}. " +
                $"Using {resolved} (header, falling back to C_livery then folder name). " +
                "Further occurrences this session are not logged.",
                new InvalidOperationException("CarId source mismatch"));
        }

        return (resolved, consistency);
    }

    private static (int CarId, string? Timestamp) ParseFolderName(string folderName)
    {
        var m = FolderNameRegex().Match(folderName);
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
}
