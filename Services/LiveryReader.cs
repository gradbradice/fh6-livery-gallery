using Forza.Data;
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

    public static string ParserVersion { get; } = ReadParserVersion();

    private static string ReadParserVersion()
    {
        try
        {
            return NativeHeaderParser.GetVersion();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to query the native fdata version", ex);
            return "?";
        }
    }

    public LiveryCacheEntry ParseLivery(string folder, LiveryFileHashes hashes, LiveryCacheEntry? previous, out uint[]? cLiverySectionCounts)
    {
        string folderName = Path.GetFileName(folder);
        var (folderCarId, tsRaw) = ParseFolderName(folderName);
        var issues = new List<LiveryParseIssue>();

        // Header: Ok, or ParseError (Value == null). A too-long string is not a failure (TruncatedField).
        var headerResult = NativeHeaderParser.TryParseHeader(hashes.HeaderData);
        var parsedHeader = headerResult.Value;
        if (headerResult.Error is { } headerError)
        {
            AddIssue(issues, folder, LiveryParseFile.Header,
                headerResult.HasValue ? LiveryParseSeverity.Partial : LiveryParseSeverity.Error, headerError);
        }
        uint? headerCarId = parsedHeader?.TargetCarId;

        byte[]? cLiveryBytes = hashes.CLiveryBytes;
        uint? cLiveryCarId;
        bool isPossiblyGenerated;
        bool hasNoLayers;
        cLiverySectionCounts = null;
        if (previous is { SchemaVersion: LiveryCacheEntry.CurrentSchemaVersion }
            && previous.ParserVersion == ParserVersion
            && hashes.CLivery is not null && hashes.CLivery == previous.CLiveryHash
            && previous.GenerationAlgorithmVersion == LiveryGenerationDetector.AlgorithmVersion)
        {
            cLiveryCarId = previous.CLiveryCarId;
            isPossiblyGenerated = previous.IsPossiblyGenerated;
            hasNoLayers = previous.HasNoLayers;
            if (previous.ParseIssues is { } previousIssues)
                issues.AddRange(previousIssues.Where(i => i.File == LiveryParseFile.CLivery));
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
            if (cLiveryBytes is not null)
            {
                try
                {
                    var liveryResult = NativeHeaderParser.TryParseCLivery(cLiveryBytes);
                    if (liveryResult.HasValue)
                    {
                        cLiveryCarId = liveryResult.Value.TargetCarId;
                        cLiverySectionCounts = [.. liveryResult.Value.SectionCounts];
                    }
                    if (liveryResult.Error is { } liveryError)
                    {
                        AddIssue(issues, folder, LiveryParseFile.CLivery,
                            liveryResult.HasValue ? LiveryParseSeverity.Partial : LiveryParseSeverity.Error, liveryError);
                    }

                    var detection = LiveryGenerationDetector.Detect(cLiveryBytes);
                    isPossiblyGenerated = detection.Verdict == LiveryGenerationVerdict.Generated;
                    hasNoLayers = detection.Verdict == LiveryGenerationVerdict.NoLayers;
                    if (detection.Error is { } detectionError)
                    {
                        AddIssue(issues, folder, LiveryParseFile.CLivery,
                            liveryResult.HasValue ? LiveryParseSeverity.Partial : LiveryParseSeverity.Error, detectionError);
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(LiveryParseIssue.Unexpected(LiveryParseFile.CLivery));
                    AppLogger.LogErrorThrottled(folder, $"Failed to parse C_livery in '{folder}'", ex);
                }
            }
        }

        bool hasParseError = issues.Any(i => i.Severity == LiveryParseSeverity.Error);

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
            ParserVersion = ParserVersion,
            ParseIssues = issues.Count > 0 ? issues : null,
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

    public static (uint[]? SectionCounts, IReadOnlyList<LiveryShapeFingerprint>? Shapes) TryReadDuplicateInputs(
        string folderPath, uint[]? knownSectionCounts)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(folderPath, "C_livery"));
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(folderPath, $"Failed to re-read C_livery for duplicate check: '{folderPath}'", ex);
            return (knownSectionCounts, null);
        }

        uint[]? sectionCounts = knownSectionCounts;
        IReadOnlyList<LiveryShapeFingerprint>? shapes = null;
        try
        {
            if (sectionCounts is null)
            {
                var livery = NativeHeaderParser.TryParseCLivery(bytes);
                if (livery.HasValue) sectionCounts = [.. livery.Value.SectionCounts];
            }

            // Partial = the tree is complete and verified, only the paint table after it is broken.
            var fingerprints = NativeHeaderParser.TryExtractShapeFingerprints(bytes);
            if (fingerprints.HasValue) shapes = fingerprints.Value;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(folderPath, $"Failed to parse C_livery for duplicate check: '{folderPath}'", ex);
        }
        return (sectionCounts, shapes);
    }

    private static void AddIssue(
        List<LiveryParseIssue> issues, string folder, LiveryParseFile file, LiveryParseSeverity severity, ParseError error)
    {
        var issue = LiveryParseIssue.From(file, severity, error);
        if (issues.Any(i => i.File == issue.File && i.Code == issue.Code && i.Level == issue.Level)) return;
        issues.Add(issue);

        // The UI shows only the code; everything needed to investigate goes here.
        string details = string.Join(", ", new[]
        {
            $"code={error.Code} ({(int)error.Code})",
            $"level={error.Level} ({(int)error.Level})",
            error.SectionIndex is int section ? $"section={error.SectionName ?? section.ToString()}" : null,
            error.ByteOffset is long offset ? $"offset={offset}" : null,
            error.Expected is long expected ? $"expected={expected}" : null,
            error.Actual is long actual ? $"actual={actual}" : null,
        }.Where(part => part is not null));

        AppLogger.LogErrorThrottled($"{folder}|{file}|{error.Code}|{error.Level}",
            $"{file} of '{Path.GetFileName(folder)}' parsed with status {severity} [{details}]: {error.Message}",
            new InvalidDataException(error.Message));
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
