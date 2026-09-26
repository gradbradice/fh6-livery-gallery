using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class AppCacheService
{
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "cache.json");
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = false };

    public string ThumbsDir { get; } = AppSettings.ThumbsPath;

    private readonly Lock _lock = new();
    private Dictionary<string, LiveryCacheEntry>? _current;
    private bool _loadFailed;

    public (Dictionary<string, LiveryCacheEntry> Cache, bool LoadFailed) Load()
    {
        lock (_lock)
        {
            if (_current is null)
            {
                (_current, _loadFailed) = LoadFromDisk();
                return (new Dictionary<string, LiveryCacheEntry>(_current), _loadFailed);
            }
            return (new Dictionary<string, LiveryCacheEntry>(_current), false);
        }
    }

    public void Save(Dictionary<string, LiveryCacheEntry> data)
    {
        var snapshot = new Dictionary<string, LiveryCacheEntry>(data);
        lock (_lock) _current = snapshot;

        try
        {
            string json = JsonSerializer.Serialize(snapshot, _writeOptions);
            PersistenceManager.Schedule(_path, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise scan cache", ex);
        }
    }

    private static (Dictionary<string, LiveryCacheEntry> Cache, bool LoadFailed) LoadFromDisk()
    {
        try
        {
            if (File.Exists(_path))
            {
                using var stream = File.OpenRead(_path);
                var data = JsonSerializer.Deserialize<Dictionary<string, LiveryCacheEntry>>(stream);
                if (data is not null) return (data, false);
                AtomicFile.TryBackupCorruptedFile(_path);
                return ([], true);
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
}
