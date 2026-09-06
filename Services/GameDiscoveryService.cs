namespace LiveryGallery.Services;

internal static class GameDiscoveryService
{
    public static string? TryFindGamePath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return GameDiscoveryServiceSteam.TryFindViaSteam() 
            ?? GameDiscoveryServiceXbox.TryFindViaXbox();
    } 
}
