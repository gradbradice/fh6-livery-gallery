using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class AppUpdateCheckService
{
    private readonly HttpClient _http;

    public AppUpdateCheckService(HttpClient http)
    {
        _http = http;
    }

    public async Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            string url = $"https://api.github.com/repos/gradbradice/fh6-livery-gallery/releases/latest";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            string json = await _http.GetStringAsync(url, timeoutCts.Token);

            var release = JsonSerializer.Deserialize<AppGitHubReleaseEntry>(json, JsonSettings.GitHubDeserializeOptions);
            if (release?.TagName is null) return new AppUpdateCheckResult(false, null, null, null);

            string latest = NormalizeVersion(release.TagName);
            bool isNewer = CompareVersions(latest, AppSettings.Version);

            return new AppUpdateCheckResult(isNewer, latest, release.HtmlUrl, release.Body);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check for updates", ex);
            return new AppUpdateCheckResult(false, null, null, null);
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
