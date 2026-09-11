using LiveryGallery.Enums;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class AppSettingsMigration
{
    public static bool Apply(AppSettingsData data)
    {
        bool changed = false;

        // DarkTheme (<= 1.1.0) → ThemeMode
        if (data.ThemeMode is null)
        {
            data.ThemeMode = data.DarkTheme ? AppThemeMode.Dark : AppThemeMode.Light;
            changed = true;
        }

        return changed;
    }
}
