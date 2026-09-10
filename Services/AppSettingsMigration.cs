using LiveryGallery.Enums;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class AppSettingsMigration
{
    public static void Apply(AppSettingsData data)
    {
        // DarkTheme (<= 1.1.0) → ThemeMode
        data.ThemeMode ??= data.DarkTheme ? AppThemeMode.Dark : AppThemeMode.Light;
    }
}
