using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class AppCacheService
{
    private readonly SaveService _saveService;
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "cache.json");
    public string ThumbsDir { get; } = AppSettings.ThumbsPath;

    public AppCacheService()
    {
        _saveService = new();
    }

    public Dictionary<string, LiveryCacheEntry> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<Dictionary<string, LiveryCacheEntry>>(json);
                if (data is not null) return data;
            }
        }
        catch
        {
            
        }

        return [];
    }

    public void Save(Dictionary<string, LiveryCacheEntry> data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonSettings.DefaultDeserializeOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch
        {
            return;
        }
    }
}
