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

    public AppSettingsData Clone() => new()
    {
        Language = Language,
        SavePath = SavePath,
        DarkTheme = DarkTheme,
        ThemeMode = ThemeMode,
        SortMode = SortMode,
        FavoriteMode = FavoriteMode,
        DuplicatesFilterMode = DuplicatesFilterMode,
        GroupingEnabled = GroupingEnabled,
        GameInstallPath = GameInstallPath,
        AutoRefreshLiveries = AutoRefreshLiveries,
        RefreshLiveriesOnButtonClick = RefreshLiveriesOnButtonClick,
        LastKnownContainerId = LastKnownContainerId,
        LastKnownContainerSavePath = LastKnownContainerSavePath,
    };

    public void CopyFrom(AppSettingsData other)
    {
        Language = other.Language;
        SavePath = other.SavePath;
        DarkTheme = other.DarkTheme;
        ThemeMode = other.ThemeMode;
        SortMode = other.SortMode;
        FavoriteMode = other.FavoriteMode;
        DuplicatesFilterMode = other.DuplicatesFilterMode;
        GroupingEnabled = other.GroupingEnabled;
        GameInstallPath = other.GameInstallPath;
        AutoRefreshLiveries = other.AutoRefreshLiveries;
        RefreshLiveriesOnButtonClick = other.RefreshLiveriesOnButtonClick;
        LastKnownContainerId = other.LastKnownContainerId;
        LastKnownContainerSavePath = other.LastKnownContainerSavePath;
    }
}
