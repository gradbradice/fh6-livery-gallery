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
            var scanService = new LiveryScanService(cacheService, carDatabase, favoriteService, tagService, authorCardService);
            var updateService = new AppUpdateCheckService(AppHttpClient.Instance);
            desktop.MainWindow = new MainWindow(
                settings, cacheService, carDatabase, tagService, favoriteService, authorCardService, scanService, updateService);
        }

        base.OnFrameworkInitializationCompleted();
    }
}