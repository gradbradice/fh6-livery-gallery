using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ForzaToolkit.Liveries.Formats;
using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.Localisation;
using LiveryGallery.Render;
using LiveryGallery.Views;
using Microsoft.Extensions.Logging;

namespace LiveryGallery;

internal partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettingsService.Load();
        AppThemeService.ApplyTheme(settings.ThemeMode ?? AppThemeMode.System);
        AppLocalisationService.AppLanguage = settings.Language;
        // The game the gallery shows: FH6. Its open parts come from the profile; the parser and the duplicate check of
        // its files (closed ForzaToolkit.Liveries.Formats) are given here
        var profile = GameProfiles.Fh6();
        profile.WarmUpInstallFolder();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var cacheService = new AppCacheService();
            // The car list and the car texts taken from the game live in the ForzaToolkit cache of the game
            // (%LOCALAPPDATA%\ForzaToolkit\fh6), shared with the other ForzaToolkit apps; the list the gallery kept in its
            // own folder before is copied there once
            var carDatabase = new CarDatabaseService(AppHttpClient.Instance, CarDatabaseOptions.ForGame(profile.Game) with
            {
                LegacyCacheFilePath = Path.Combine(AppSettings.BaseCachePath, CarDatabaseSources.FileName(profile.Game)),
            });
            // The paints of the livery editor (finish code -> game material) live in the same cache; the 3D renderer reads
            // them there, so a paint the game adds or changes needs no new gallery version. Checked once per start
            var liveryMaterials = new LiveryMaterialsService(AppHttpClient.Instance, LiveryMaterialsOptions.ForGame(profile.Game),
                AppLogger.LoggerFactory.CreateLogger<LiveryMaterialsService>());
            _ = Task.Run(() => liveryMaterials.RefreshAsync());
            var carTexts = new CarTextProvider(carDatabase,
                new CarLocalizationService(CarLocalizationOptions.ForGame(profile.Game), AppLogger.LoggerFactory.CreateLogger<CarLocalizationService>()),
                settings);
            var tagService = new TagService(AppSettings.BaseCachePath);
            var favoriteService = new FavoriteService(AppSettings.BaseCachePath);
            // Shared with the other ForzaToolkit apps (%APPDATA%\ForzaToolkit); the first start copies the app's old cards there
            var authorCardService = SharedAuthorCards.Open(AppSettings.BaseCachePath, PlaceholderTexts.IsUnknownAuthor);
            var saveFolderLock = new SaveFolderLock();
            var liveryStore = profile.Liveries;
            var archiveService = new LiveryArchive(new LiveryArchiveOptions
            {
                Directory = AppSettings.ArchivePath,
                Store = liveryStore,
                SaveFolderLock = saveFolderLock,
                RecoveredEntry = RecoveredArchiveEntry,
                Logger = AppLogger.LoggerFactory.CreateLogger<LiveryArchive>(),
            });
            var backupService = new LiveryBackups(new LiveryBackupsOptions
            {
                Directory = AppSettings.BackupsPath,
                Store = liveryStore,
                SaveFolderLock = saveFolderLock,
                Favorites = favoriteService,
                Tags = tagService,
                Logger = AppLogger.LoggerFactory.CreateLogger<LiveryBackups>(),
            });
            var liveryIdService = new LiveryIdService(AppSettings.BaseCachePath);
            var scanService = new LiveryScanner(cacheService, carDatabase, favoriteService, tagService, authorCardService,
                archiveService, saveFolderLock, liveryIdService, profile, Fh6ScanParts());
            var updateService = new AppUpdateCheckService(AppHttpClient.Instance);
            // The closed side: 3D viewer, game folder check, versions and parse error names (LiveryGallery.Render on stage 5)
            var viewer3D = new Livery3DViewer(settings);
            LiveryParseIssueFormatter.CodeNames = RenderComponents.ParseErrorName;
            var diagnosticsService = new DiagnosticsExportService(cacheService);
            var accountRegistry = new AccountRegistry(cacheService, profile.Saves, AccountLabels.Title);
            desktop.MainWindow = new MainWindow(
                settings, profile, new LiveryServerService(), carDatabase, carTexts, tagService, favoriteService, authorCardService, archiveService, backupService,
                scanService, updateService, viewer3D, new RenderGameFolderCheck(), RenderComponents.ReadVersions(),
                saveFolderLock, diagnosticsService, accountRegistry);
            desktop.Exit += (_, _) => viewer3D.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The closed parts of the FH6 scan: the parser of header and C_livery, and the duplicate check with its cache</summary>
    private static LiveryScanParts Fh6ScanParts()
    {
        var parser = new Fh6LiveryParser(AppLogger.LoggerFactory.CreateLogger<Fh6LiveryParser>());
        var duplicates = new Fh6DuplicateDetector(
            Path.Combine(AppSettings.BaseCachePath, "duplicate-inputs"), parser.Version,
            AppLogger.LoggerFactory.CreateLogger<Fh6DuplicateDetector>());
        return new LiveryScanParts(parser, duplicates);
    }

    /// <summary>A folder in the archive that its index lost: the key as the name, the rest unknown</summary>
    private static LiveryData RecoveredArchiveEntry(string key) => new()
    {
        FolderName = key,
        LiveryName = key,
        AuthorRaw = string.Empty, // shown as "Unknown" in the current language
        TextVersion = LiveryCacheEntry.CurrentTextVersion,
        CarId = 0,
        CarManufacturerRaw = Strings.ArchiveRecoveredUnknownValue,
        CarModelNameRaw = "",
        CarKnown = false,
        HasThumbnail = false,
    };
}