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
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise settings (Save)", ex);
        }
    }

    public static void SaveImmediate(AppSettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultOptions);
            _saveService.SaveImmediate(json, _path);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise settings (SaveImmediate)", ex);
        }
    }

    public static void Flush() => _saveService.Flush();

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
                    AppSettingsMigration.Apply(data);
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load settings", ex);
            AtomicFile.TryBackupCorruptedFile(_path);
        }

        return new AppSettingsData
        {
            Language = AppLocalisationService.GetSystemLanguage(),
            ThemeMode = AppThemeMode.System
        };
    }
}
