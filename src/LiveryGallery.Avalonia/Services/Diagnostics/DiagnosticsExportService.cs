using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveryGallery.Services.Diagnostics;

/// <summary>
/// Builds the ZIP for "Export diagnostics": a README, logs, settings without paths and IDs, and the parse issues
/// of the liveries (listed by folder name). Nothing is sent anywhere; the user decides where the file goes.
/// </summary>
internal sealed class DiagnosticsExportService(AppCacheService cacheService)
{
    // The tail is what matters; a runaway log should not make a huge archive
    private const int MaxLogBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SuggestedFileName(DateTime localNow) =>
        $"{AppSettings.Identity.FolderName}-diagnostics-{localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip";

    /// <summary>Call on the UI thread: the settings are copied here, the archive is built on a worker thread</summary>
    public async Task WriteZipAsync(Stream destination, DiagnosticsSources sources, CancellationToken ct)
    {
        var snapshot = sources with { Settings = sources.Settings.Clone() };
        string name = AppSettings.Identity.Name;
        string readme = BuildReadme(
            AppLocalisationService.AppLanguage,
            string.Format(Strings.DiagnosticsReadmeFormat, name),
            string.Format(Strings.ResourceManager.GetString(nameof(Strings.DiagnosticsReadmeFormat), CultureInfo.InvariantCulture)
                ?? Strings.DiagnosticsReadmeFormat, name));
        byte[] zip = await Task.Run(() => BuildZip(snapshot, readme, ct), ct);

        await destination.WriteAsync(zip, ct);
        if (destination.CanSeek) destination.SetLength(zip.Length); // an overwritten longer file must not keep its tail
        await destination.FlushAsync(ct);
    }

    /// <summary>
    /// The README is read by the user before sending and by the developer after: in the interface language with
    /// the English text below it, or English alone when that is the interface language (or no translation exists)
    /// </summary>
    internal static string BuildReadme(AppLanguage language, string localized, string english)
    {
        string text = language == AppLanguage.English || string.Equals(localized.Trim(), english.Trim(), StringComparison.Ordinal)
            ? english.Trim()
            : localized.Trim() + "\n\n" + new string('-', 60) + "\n\n" + english.Trim();
        return text.ReplaceLineEndings("\r\n") + "\r\n";
    }

    private byte[] BuildZip(DiagnosticsSources sources, string readme, CancellationToken ct)
    {
        var redactor = DiagnosticsRedactor.ForCurrentUser(sources);
        var (cache, cacheLoadFailed) = sources.AccountKey is { } accountKey
            ? cacheService.Load(accountKey)
            : (new Dictionary<string, LiveryCacheEntry>(), false);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // With a BOM, so that older viewers do not show the translated part as mojibake
            AddText(zip, "README.txt", readme, withBom: true);
            AddText(zip, "info.json", redactor.Redact(BuildInfo(sources.Settings, cache.Count, cacheLoadFailed)));
            AddText(zip, "settings.json",
                redactor.Redact(DiagnosticsSettingsSanitizer.Sanitize(sources.Settings, JsonOptions).ToJsonString(JsonOptions)));
            AddText(zip, "parse-issues.json", redactor.Redact(BuildParseIssues(cache.Values)));
            AddLogs(zip, redactor, ct);
        }
        return buffer.ToArray();
    }

    private static string BuildInfo(AppSettingsData settings, int cachedLiveries, bool cacheLoadFailed) =>
        JsonSerializer.Serialize(new
        {
            AppVersion = AppSettings.Version,
            ExportedUtc = DateTime.UtcNow,
            Os = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Culture = CultureInfo.CurrentCulture.Name,
            UiLanguage = settings.Language.ToString(),
            CachedLiveries = cachedLiveries,
            CacheLoadFailed = cacheLoadFailed,
        }, JsonOptions);

    private static string BuildParseIssues(IEnumerable<LiveryCacheEntry> cacheEntries)
    {
        var all = cacheEntries.ToList();
        var affected = all
            .Where(e => e.HasParseError || e.ParseIssues is { Count: > 0 })
            .OrderBy(e => e.FolderName, StringComparer.Ordinal)
            .Select(e => new
            {
                e.FolderName,
                e.CarId,
                e.ParserVersion,
                e.HasParseError,
                Issues = e.ParseIssues ?? [],
            })
            .ToList();

        var byKind = affected
            .SelectMany(e => e.Issues)
            .GroupBy(i => $"{i.File}/{i.Severity}/{i.Code}")
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

        return JsonSerializer.Serialize(new
        {
            TotalLiveries = all.Count,
            WithErrors = affected.Count(e => e.HasParseError),
            WithPartialIssuesOnly = affected.Count(e => !e.HasParseError),
            IssuesByKind = byKind,
            Liveries = affected,
        }, JsonOptions);
    }

    private static void AddLogs(ZipArchive zip, DiagnosticsRedactor redactor, CancellationToken ct)
    {
        string logsPath = AppSettings.LogsPath;
        if (!Directory.Exists(logsPath)) return;

        foreach (string path in Directory.EnumerateFiles(logsPath, "*.log").Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            AddText(zip, "logs/" + Path.GetFileName(path), redactor.Redact(ReadLog(path)));
        }
    }

    private static string ReadLog(string path)
    {
        try
        {
            // The logger keeps today's file open (shared), so it is read without locking it
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string prefix = "";
            if (stream.Length > MaxLogBytes)
            {
                stream.Seek(-MaxLogBytes, SeekOrigin.End);
                prefix = $"[diagnostics: only the last {MaxLogBytes / (1024 * 1024)} MB of {stream.Length / (1024 * 1024)} MB]{Environment.NewLine}";
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return prefix + reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"[diagnostics: could not read this log: {ex.GetType().Name}: {ex.Message}]";
        }
    }

    private static void AddText(ZipArchive zip, string entryName, string text, bool withBom = false)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
        writer.Write(text);
    }
}
