using LiveryGallery.Enums;

namespace LiveryGallery.Models;

internal class AppSettingsData
{
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public string? SavePath { get; set; }
    // DarkTheme for backward compatibility (<= 1.1.0)
    public bool DarkTheme { get; set; }
    public AppThemeMode? ThemeMode { get; set; }
    public SortMode SortMode { get; set; } = SortMode.Manufacture;
    public FavoriteMode FavoriteMode { get; set; } = FavoriteMode.None;
    public DuplicatesFilterMode DuplicatesFilterMode { get; set; } = DuplicatesFilterMode.All;
    public bool GroupingEnabled { get; set; } = true;
    public string? GameInstallPath { get; set; }
    public bool AutoRefreshLiveries { get; set; } = true;
    public bool RefreshLiveriesOnButtonClick { get; set; } = false;
    public int? LastKnownContainerId { get; set; }
    public string? LastKnownContainerSavePath { get; set; }
}
