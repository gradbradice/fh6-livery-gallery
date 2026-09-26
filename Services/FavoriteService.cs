using LiveryGallery.Configuration;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class FavoriteService
{
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "favorites.json");
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new();

    public FavoriteService() => Load();

    public bool IsFavorite(string folderName)
    {
        lock (_lock) return _favorites.Contains(folderName);
    }

    public void SetFavorite(string folderName, bool isFavorite)
    {
        lock (_lock)
        {
            _ = isFavorite ? _favorites.Add(folderName) : _favorites.Remove(folderName);
        }
        Save();
    }

    private void Save()
    {
        try
        {
            lock (_saveLock)
            {
                List<string> snapshot;
                lock (_lock) snapshot = _favorites.ToList();
                string json = JsonSerializer.Serialize(snapshot, JsonSettings.DefaultOptions);
                PersistenceManager.Schedule(_path, json);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise favorites", ex);
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<List<string>>(json);
                if (data != null) _favorites = new HashSet<string>(data, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load favorites", ex);
            AtomicFile.TryBackupCorruptedFile(_path);
        }
    }
}
