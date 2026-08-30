using Avalonia;
using Avalonia.Styling;

namespace LiveryGallery.Services;

internal static class AppThemeService
{
    public static bool IsDarkTheme { get; private set; }

    public static void Initialise()
    {
        var settings = AppSettingsService.Load();
        ApplyTheme(settings.DarkTheme, persist: false);
    }

    public static void ApplyTheme(bool dark, bool persist = true)
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        IsDarkTheme = dark;

        if (persist)
        {
            var settings = AppSettingsService.Load();
            settings.DarkTheme = dark;
            AppSettingsService.Save(settings);
        }
    }

    public static void ToggleTheme() => ApplyTheme(!IsDarkTheme);
}
