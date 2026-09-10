namespace LiveryGallery.Services;

internal static class GameDiscoveryService
{
    private static readonly Lazy<string?> _cachedPath = new(() =>
        OperatingSystem.IsWindows()
            ? GameDiscoveryServiceSteam.TryFindViaSteam() ?? GameDiscoveryServiceXbox.TryFindViaXbox()
            : null);

    public static string? TryFindGamePath() => _cachedPath.Value;
    public static void WarmUpInBackground() => _ = Task.Run(() => _cachedPath.Value);
}
