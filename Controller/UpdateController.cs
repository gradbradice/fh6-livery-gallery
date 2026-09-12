using Avalonia.Controls;
using LiveryGallery.Services;
using LiveryGallery.Views;

namespace LiveryGallery.Controller;

internal sealed class UpdateController(AppUpdateCheckService updateService)
{
    public string? LatestVersion { get; private set; }
    public string? ReleaseUrl { get; private set; }
    public string? ReleaseBody { get; private set; }

    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        var result = await updateService.CheckAsync(ct);
        if (!result.IsNewer || result.LatestVersion is null) return false;

        LatestVersion = result.LatestVersion;
        ReleaseUrl = result.ReleaseUrl;
        ReleaseBody = result.ReleaseBody;
        return true;
    }

    public async Task ShowDetailsAsync(Window owner)
    {
        if (LatestVersion is null) return;
        var dialog = new WhatsNewDialog(LatestVersion, ReleaseBody, ReleaseUrl);
        await dialog.ShowDialog(owner);
    }
}
