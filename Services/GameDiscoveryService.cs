namespace LiveryGallery.Services;

internal static class GameDiscoveryService
{
    private static readonly Lazy<Task<string?>> _cachedPathTask = new(() => Task.Run(() =>
        OperatingSystem.IsWindows()
            ? GameDiscoveryServiceSteam.TryFindViaSteam() ?? GameDiscoveryServiceXbox.TryFindViaXbox()
            : null));
    
    public static Task<string?> TryFindGamePathAsync() => _cachedPathTask.Value;

    public static void WarmUpInBackground() => _ = _cachedPathTask.Value;
}
