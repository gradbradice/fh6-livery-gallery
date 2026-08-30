namespace LiveryGallery.Models;

internal record AppUpdateCheckResult(
    bool IsNewer, 
    string? LatestVersion, 
    string? ReleaseUrl, 
    string? ReleaseBody);