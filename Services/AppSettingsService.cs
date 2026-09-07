using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal static class AppSettingsService
{
    private static readonly SaveService _saveService = new();
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "settings.json");

    public static void Save(AppSettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultDeserializeOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch
        {

        }
    }

    public static void SaveImmediate(AppSettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultDeserializeOptions);
            _saveService.SaveImmediate(json, _path);
        }
        catch
        {

        }
    }

    public static AppSettingsData Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                if (data != null)
                {
                    // backward compatibility
                    data.ThemeMode ??= data.DarkTheme ? AppThemeMode.Dark : AppThemeMode.Light;
                    return data;
                }
            }
        }
        catch
        {

        }

        return new AppSettingsData
        {
            Language = AppLocalisationService.GetSystemLanguage(),
            ThemeMode = AppThemeMode.System
        };
    }

}
