using System.Diagnostics;

namespace LiveryGallery.Services;

internal static class TrustedUrlLauncher
{
    private static readonly string[] AllowedHosts =
    [
        "twitter.com", "www.twitter.com", "x.com", "www.x.com", "mobile.twitter.com",
        "youtube.com", "www.youtube.com", "m.youtube.com",
    ];

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
