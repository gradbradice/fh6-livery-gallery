using LiveryGallery.Enums;
using LiveryGallery.Services;

namespace LiveryGallery.Models;

internal class AppSettingsData
{
    public AppLanguage Language { get; set; } = AppLanguage.English;

    /// <summary>
    /// The game language car names, countries and car types are shown in (EN, RU, CHS…, see GameLanguages);
    /// null: the game language that matches the interface language
    /// </summary>
    public string? CarTextLanguage { get; set; }
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

    /// <summary>The range and default are every ForzaToolkit app's (<see cref="PossibleDuplicateThreshold"/>)</summary>
    public int PossibleDuplicateThresholdPercent { get; set; } = PossibleDuplicateThreshold.DefaultPercent;
    public bool GroupingEnabled { get; set; } = true;
    public string? GameInstallPath { get; set; }
    public int ViewerQualityIndex { get; set; } = 2;
    public bool AutoRefreshLiveries { get; set; } = true;
    public bool RefreshLiveriesOnButtonClick { get; set; } = false;
    public bool SearchByFolderName { get; set; } = false;
    public int? LastKnownContainerId { get; set; }
    public string? LastKnownContainerSavePath { get; set; }

    // In-game navigation ("Find in game")
    public MyDesignsRowsMode MyDesignsRowsMode { get; set; } = MyDesignsRowsMode.Auto;
    public int NavigationKeyIntervalMs { get; set; } = 65;
    public int NavigationKeyHoldMs { get; set; } = 15;
    public int NavigationSwitchDelayMs { get; set; } = 500;
    public int NavigationTurnDelayMs { get; set; } = 200;
    public int NavigationWrapDelayMs { get; set; } = 800;
    public bool NavigationReopenMenu { get; set; } = true;
    public int NavigationMenuBackDelayMs { get; set; } = 1000;
    public int NavigationMenuOpenDelayMs { get; set; } = 2000;

    public bool ShowChangesSinceLastLaunch { get; set; } = true;

    /// <summary>After the first scan of a launch, say when little room is left in the save (the "Room in the save" window)</summary>
    public bool WarnFewFreeSlots { get; set; } = true;

    // Automatic backups (AutoBackups)
    public bool AutoBackupBeforeArchive { get; set; } = true;
    public bool AutoBackupOnChange { get; set; } = true;
    public bool AutoBackupPeriodic { get; set; }
    public int AutoBackupPeriodDays { get; set; } = AutoBackupPolicy.DefaultPeriodDays;
    public int AutoBackupKeep { get; set; } = AutoBackupPolicy.DefaultKeep;

    public AppSettingsData Clone() => new()
    {
        Language = Language,
        CarTextLanguage = CarTextLanguage,
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
        MyDesignsRowsMode = MyDesignsRowsMode,
        NavigationKeyIntervalMs = NavigationKeyIntervalMs,
        NavigationKeyHoldMs = NavigationKeyHoldMs,
        NavigationSwitchDelayMs = NavigationSwitchDelayMs,
        NavigationTurnDelayMs = NavigationTurnDelayMs,
        NavigationWrapDelayMs = NavigationWrapDelayMs,
        NavigationReopenMenu = NavigationReopenMenu,
        NavigationMenuBackDelayMs = NavigationMenuBackDelayMs,
        NavigationMenuOpenDelayMs = NavigationMenuOpenDelayMs,
        ShowChangesSinceLastLaunch = ShowChangesSinceLastLaunch,
        WarnFewFreeSlots = WarnFewFreeSlots,
        AutoBackupBeforeArchive = AutoBackupBeforeArchive,
        AutoBackupOnChange = AutoBackupOnChange,
        AutoBackupPeriodic = AutoBackupPeriodic,
        AutoBackupPeriodDays = AutoBackupPeriodDays,
        AutoBackupKeep = AutoBackupKeep,
    };

    public void CopyFrom(AppSettingsData other)
    {
        Language = other.Language;
        CarTextLanguage = other.CarTextLanguage;
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
        MyDesignsRowsMode = other.MyDesignsRowsMode;
        NavigationKeyIntervalMs = other.NavigationKeyIntervalMs;
        NavigationKeyHoldMs = other.NavigationKeyHoldMs;
        NavigationSwitchDelayMs = other.NavigationSwitchDelayMs;
        NavigationTurnDelayMs = other.NavigationTurnDelayMs;
        NavigationWrapDelayMs = other.NavigationWrapDelayMs;
        NavigationReopenMenu = other.NavigationReopenMenu;
        NavigationMenuBackDelayMs = other.NavigationMenuBackDelayMs;
        NavigationMenuOpenDelayMs = other.NavigationMenuOpenDelayMs;
        ShowChangesSinceLastLaunch = other.ShowChangesSinceLastLaunch;
        WarnFewFreeSlots = other.WarnFewFreeSlots;
        AutoBackupBeforeArchive = other.AutoBackupBeforeArchive;
        AutoBackupOnChange = other.AutoBackupOnChange;
        AutoBackupPeriodic = other.AutoBackupPeriodic;
        AutoBackupPeriodDays = other.AutoBackupPeriodDays;
        AutoBackupKeep = other.AutoBackupKeep;
    }
}
