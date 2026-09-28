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
    public MineMode MineMode { get; set; } = MineMode.None;
    public InstalledMode InstalledMode { get; set; } = InstalledMode.None;
    public DuplicatesFilterMode DuplicatesFilterMode { get; set; } = DuplicatesFilterMode.All;
    public GeneratedFilterMode GeneratedFilterMode { get; set; } = GeneratedFilterMode.All;
    public PaintFilterMode PaintFilterMode { get; set; } = PaintFilterMode.All;
    public AuctionFilterMode AuctionFilterMode { get; set; } = AuctionFilterMode.All;

    public bool ServerConnectionEnabled { get; set; }

    public bool WarnArchiveWithoutInstalledData { get; set; } = true;

    public bool WarnArchiveWithOutdatedInstalledData { get; set; } = true;

    public const int MinPossibleDuplicateThresholdPercent = 50;
    public const int MaxPossibleDuplicateThresholdPercent = 90;
    public const int DefaultPossibleDuplicateThresholdPercent = 70;
    public int PossibleDuplicateThresholdPercent { get; set; } = DefaultPossibleDuplicateThresholdPercent;

    public static int ClampPossibleDuplicateThreshold(int percent) =>
        Math.Clamp(percent, MinPossibleDuplicateThresholdPercent, MaxPossibleDuplicateThresholdPercent);
    public bool GroupingEnabled { get; set; } = true;
    public string? GameInstallPath { get; set; }
    public int ViewerQualityIndex { get; set; } = 2;
    public bool AutoRefreshLiveries { get; set; } = true;
    public bool RefreshLiveriesOnButtonClick { get; set; } = false;
    public bool SearchByFolderName { get; set; } = false;
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
        MineMode = MineMode,
        InstalledMode = InstalledMode,
        DuplicatesFilterMode = DuplicatesFilterMode,
        GeneratedFilterMode = GeneratedFilterMode,
        PaintFilterMode = PaintFilterMode,
        AuctionFilterMode = AuctionFilterMode,
        PossibleDuplicateThresholdPercent = PossibleDuplicateThresholdPercent,
        ServerConnectionEnabled = ServerConnectionEnabled,
        WarnArchiveWithoutInstalledData = WarnArchiveWithoutInstalledData,
        WarnArchiveWithOutdatedInstalledData = WarnArchiveWithOutdatedInstalledData,
        GroupingEnabled = GroupingEnabled,
        GameInstallPath = GameInstallPath,
        ViewerQualityIndex = ViewerQualityIndex,
        AutoRefreshLiveries = AutoRefreshLiveries,
        RefreshLiveriesOnButtonClick = RefreshLiveriesOnButtonClick,
        SearchByFolderName = SearchByFolderName,
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
        MineMode = other.MineMode;
        InstalledMode = other.InstalledMode;
        DuplicatesFilterMode = other.DuplicatesFilterMode;
        GeneratedFilterMode = other.GeneratedFilterMode;
        PaintFilterMode = other.PaintFilterMode;
        AuctionFilterMode = other.AuctionFilterMode;
        PossibleDuplicateThresholdPercent = other.PossibleDuplicateThresholdPercent;
        ServerConnectionEnabled = other.ServerConnectionEnabled;
        WarnArchiveWithoutInstalledData = other.WarnArchiveWithoutInstalledData;
        WarnArchiveWithOutdatedInstalledData = other.WarnArchiveWithOutdatedInstalledData;
        GroupingEnabled = other.GroupingEnabled;
        GameInstallPath = other.GameInstallPath;
        ViewerQualityIndex = other.ViewerQualityIndex;
        AutoRefreshLiveries = other.AutoRefreshLiveries;
        RefreshLiveriesOnButtonClick = other.RefreshLiveriesOnButtonClick;
        SearchByFolderName = other.SearchByFolderName;
        LastKnownContainerId = other.LastKnownContainerId;
        LastKnownContainerSavePath = other.LastKnownContainerSavePath;
    }
}
