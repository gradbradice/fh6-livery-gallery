using System.Diagnostics;

namespace LiveryGallery.Services;

internal static class TrustedUrlLauncher
{
    public static readonly IReadOnlyList<string> TwitterHosts =
        ["twitter.com", "www.twitter.com", "x.com", "www.x.com", "mobile.twitter.com"];

    public static readonly IReadOnlyList<string> YouTubeHosts =
        ["youtube.com", "www.youtube.com", "m.youtube.com"];

    public static readonly IReadOnlyList<string> GitHubHosts = ["github.com", "www.github.com"];

    private static readonly string[] AllowedHosts = [.. TwitterHosts, .. YouTubeHosts, .. GitHubHosts];

    public static void TryOpen(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttps) return;
        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            
        }
    }
}
