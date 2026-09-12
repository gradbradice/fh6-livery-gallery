using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Net;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class AppUpdateCheckService
{
    private static readonly string _metaPath = Path.Combine(AppSettings.BaseCachePath, "update-check.meta.json");
    private readonly HttpClient _http;
    private readonly Lock _lock = new();
    private AppUpdateCheckMetadata _metadata;
    private Task<AppUpdateCheckResult>? _inFlightCheck;
    private readonly object _checkGate = new();

    public AppUpdateCheckService(HttpClient http)
    {
        _http = http;
        _metadata = LoadMetadata();
    }

    public Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        Task<AppUpdateCheckResult> inFlight;
        lock (_checkGate)
        {
            _inFlightCheck ??= CheckCoreAsync(ct);
            inFlight = _inFlightCheck;
        }

        return ct.CanBeCanceled ? WaitWithOwnCancellation(inFlight, ct) : inFlight;
    }

    private static async Task<AppUpdateCheckResult> WaitWithOwnCancellation(
        Task<AppUpdateCheckResult> inFlight, CancellationToken ct)
    {
        var cancellationTask = Task.Delay(Timeout.Infinite, ct);
        var completed = await Task.WhenAny(inFlight, cancellationTask);
        if (completed == cancellationTask)
            ct.ThrowIfCancellationRequested();
        return await inFlight;
    }

    private async Task<AppUpdateCheckResult> CheckCoreAsync(CancellationToken ct)
    {
        try
        {
            string url = "https://api.github.com/repos/gradbradice/fh6-livery-gallery/releases/latest";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            string? currentETag;
            lock (_lock) currentETag = _metadata.ETag;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(currentETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", currentETag);

            using var response = await _http.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                AppUpdateCheckMetadata snapshot;
                lock (_lock) snapshot = _metadata;
                return BuildResult(snapshot.LatestVersion, snapshot.ReleaseUrl, snapshot.ReleaseBody);
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            string? newETag = response.Headers.ETag?.Tag;

            var release = JsonSerializer.Deserialize<AppGitHubReleaseEntry>(json, JsonSettings.GitHubDeserializeOptions);
            if (release?.TagName is null) return new AppUpdateCheckResult(false, null, null, null);

            string latest = NormalizeVersion(release.TagName);

            var updated = new AppUpdateCheckMetadata
            {
                ETag = newETag,
                LatestVersion = latest,
                ReleaseUrl = release.HtmlUrl,
                ReleaseBody = release.Body,
            };
            lock (_lock) _metadata = updated;
            await SaveMetadataAsync(updated);

            return BuildResult(latest, release.HtmlUrl, release.Body);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check for updates", ex);
            return new AppUpdateCheckResult(false, null, null, null);
        }
        finally
        {
            lock (_checkGate) _inFlightCheck = null;
        }
    }

    private static AppUpdateCheckResult BuildResult(string? latest, string? url, string? body)
    {
        if (latest is null) return new AppUpdateCheckResult(false, null, null, null);
        bool isNewer = CompareVersions(latest, AppSettings.Version);
        return new AppUpdateCheckResult(isNewer, latest, url, body);
    }

    private static AppUpdateCheckMetadata LoadMetadata()
    {
        try
        {
            if (!File.Exists(_metaPath)) return new AppUpdateCheckMetadata();
            string json = File.ReadAllText(_metaPath);
            var data = JsonSerializer.Deserialize<AppUpdateCheckMetadata>(json, JsonSettings.DefaultOptions);
            if (data is null) return new AppUpdateCheckMetadata();
            if (data.ETag is not null && data.LatestVersion is null)
            {
                AppLogger.LogError(
                    "Update-check metadata is inconsistent, resetting",
                    new InvalidDataException("ETag present without LatestVersion"));
                return new AppUpdateCheckMetadata();
            }
            return data;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load update-check metadata", ex);
            return new AppUpdateCheckMetadata();
        }
    }

    private static async Task SaveMetadataAsync(AppUpdateCheckMetadata metadata)
    {
        try
        {
            string json = JsonSerializer.Serialize(metadata, JsonSettings.DefaultOptions);
            await AtomicFile.WriteAllTextAsync(_metaPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save update-check metadata", ex);
        }
    }

    private static string NormalizeVersion(string tag) => tag.TrimStart('v', 'V');

    private static bool CompareVersions(string a, string b)
    {
        var pa = ParseParts(a);
        var pb = ParseParts(b);
        for (int i = 0; i < 3; i++)
        {
            int cmp = pa[i].CompareTo(pb[i]);
            if (cmp != 0) return cmp > 0;
        }
        return false;
    }

    private static int[] ParseParts(string v)
    {
        string core = v.Split('-', '+')[0];
        string[] raw = core.Split('.');
        var result = new int[3];
        for (int i = 0; i < 3 && i < raw.Length; i++)
            _ = int.TryParse(raw[i], out result[i]);
        return result;
    }
}
