using Avalonia;
using Avalonia.Styling;
using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class AppThemeService
{
    public static void ApplyTheme(AppThemeMode mode)
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Dark => ThemeVariant.Dark,
            AppThemeMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }
}
