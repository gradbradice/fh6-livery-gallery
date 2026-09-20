using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Views;

namespace LiveryGallery.ViewModels;

internal sealed partial class UpdateViewModel(AppUpdateCheckService updateService) : ObservableObject
{
    public string? LatestVersion { get; private set; }
    public string? ReleaseUrl { get; private set; }
    public string? ReleaseBody { get; private set; }

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _bannerText;

    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        var result = await updateService.CheckAsync(ct);
        if (!result.IsNewer || result.LatestVersion is null) return false;

        LatestVersion = result.LatestVersion;
        ReleaseUrl = result.ReleaseUrl;
        ReleaseBody = result.ReleaseBody;
        BannerText = string.Format(Strings.UpdateAvailableFormat, LatestVersion);
        IsUpdateAvailable = true;
        return true;
    }

    public void RefreshLocalizedBannerText()
    {
        if (LatestVersion is not null)
            BannerText = string.Format(Strings.UpdateAvailableFormat, LatestVersion);
    }

    public async Task ShowDetailsAsync(Window owner)
    {
        if (LatestVersion is null) return;
        var dialog = new WhatsNewDialog(LatestVersion, ReleaseBody, ReleaseUrl);
        await dialog.ShowDialog(owner);
    }
}
