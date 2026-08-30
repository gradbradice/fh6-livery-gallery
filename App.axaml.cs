using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveryGallery.Services;
using LiveryGallery.Views;

namespace LiveryGallery;

internal partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppThemeService.Initialise();
        var settings = AppSettingsService.Load();
        AppLocalisationService.AppLanguage = settings.Language;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}