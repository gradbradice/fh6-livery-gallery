using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveryGallery.Enums;
using LiveryGallery.Services;
using LiveryGallery.Views;

namespace LiveryGallery;

internal partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettingsService.Load();
        AppThemeService.ApplyTheme(settings.ThemeMode ?? AppThemeMode.System);
        AppLocalisationService.AppLanguage = settings.Language;
        GameDiscoveryService.WarmUpInBackground();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var cacheService = new AppCacheService();
            var carDatabase = new CarDatabaseService(AppHttpClient.Instance);
            var tagService = new TagService();
            var favoriteService = new FavoriteService();
            var authorCardService = new AuthorCardService();
            var saveFolderLock = new SaveFolderLock();
            var archiveService = new LiveryArchiveService(saveFolderLock);
            var backupService = new LiveryBackupService(saveFolderLock);
            var liveryIdService = new LiveryIdService();
            var scanService = new LiveryScanner(cacheService, carDatabase, favoriteService, tagService, authorCardService, archiveService, saveFolderLock, liveryIdService);
            var updateService = new AppUpdateCheckService(AppHttpClient.Instance);
            desktop.MainWindow = new MainWindow(
                settings, carDatabase, tagService, favoriteService, authorCardService, archiveService, backupService,
                scanService, updateService);
        }

        base.OnFrameworkInitializationCompleted();
    }
}