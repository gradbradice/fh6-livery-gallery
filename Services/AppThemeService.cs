using Avalonia;
using Avalonia.Styling;
using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class AppThemeService
{
    public static void Initialise()
    {
        var settings = AppSettingsService.Load();
        ApplyTheme(settings.ThemeMode ?? AppThemeMode.System, persist: false);
    }

    public static void ApplyTheme(AppThemeMode mode, bool persist = true)
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Dark => ThemeVariant.Dark,
            AppThemeMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        if (persist)
        {
            var settings = AppSettingsService.Load();
            settings.ThemeMode = mode;
            AppSettingsService.Save(settings);
        }
    }
}
