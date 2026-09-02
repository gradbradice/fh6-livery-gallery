using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal static class AppUpdateCheckService
{
    public static async Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FH6-Livery-Gallery-UpdateCheck/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string url = $"https://api.github.com/repos/gradbradice/fh6-livery-gallery/releases/latest";
            string json = await http.GetStringAsync(url, ct);

            var release = JsonSerializer.Deserialize<AppGitHubReleaseEntry>(json, JsonSettings.GitHubDeserializeOptions);
            if (release?.TagName is null) return new AppUpdateCheckResult(false, null, null, null);

            string latest = NormalizeVersion(release.TagName);
            bool isNewer = CompareVersions(latest, AppSettings.Version);

            return new AppUpdateCheckResult(isNewer, latest, release.HtmlUrl, release.Body);
        }
        catch
        {
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
