using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class AppCacheService
{
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "cache.json");
    public string ThumbsDir { get; } = AppSettings.ThumbsPath;

    public (Dictionary<string, LiveryCacheEntry> Cache, bool LoadFailed) Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<Dictionary<string, LiveryCacheEntry>>(json);
                if (data is not null) return (data, false);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load scan cache", ex);
            AtomicFile.TryBackupCorruptedFile(_path);
            return ([], true);
        }

        return ([], false);
    }

    public void Save(Dictionary<string, LiveryCacheEntry> data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultOptions);
            PersistenceManager.Schedule(_path, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise scan cache", ex);
        }
    }
}
