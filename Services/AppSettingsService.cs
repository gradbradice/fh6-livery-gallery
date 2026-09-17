using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal static class AppSettingsService
{
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "settings.json");

    public static void Save(AppSettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultOptions);
            PersistenceManager.Schedule(_path, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise settings (Save)", ex);
        }
    }

    public static async Task<bool> SaveImmediateAsync(AppSettingsData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultOptions);
            return await PersistenceManager.SaveNowAsync(_path, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise settings (SaveImmediateAsync)", ex);
            return false;
        }
    }

    private static bool NormalizeEnums(AppSettingsData data)
    {
        bool changed = false;
        if (!Enum.IsDefined(data.SortMode)) { data.SortMode = SortMode.Manufacture; changed = true; }
        if (!Enum.IsDefined(data.FavoriteMode)) { data.FavoriteMode = FavoriteMode.None; changed = true; }
        if (!Enum.IsDefined(data.MineMode)) { data.MineMode = MineMode.None; changed = true; }
        if (!Enum.IsDefined(data.DuplicatesFilterMode)) { data.DuplicatesFilterMode = DuplicatesFilterMode.All; changed = true; }
        if (!Enum.IsDefined(data.GeneratedFilterMode)) { data.GeneratedFilterMode = GeneratedFilterMode.All; changed = true; }
        if (data.ThemeMode is { } theme && !Enum.IsDefined(theme)) { data.ThemeMode = AppThemeMode.System; changed = true; }
        return changed;
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
                    bool changed = AppSettingsMigration.Apply(data);
                    changed |= NormalizeEnums(data);
                    if (changed) Save(data);
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
