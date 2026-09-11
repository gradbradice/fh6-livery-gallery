using LiveryGallery.Configuration;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class TagService
{
    private readonly SaveService _saveService;
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "tags.json");
    private Dictionary<string, List<string>> _data = [];
    private readonly Lock _lock = new();

    public TagService()
    {
        _saveService = new();
        Load();
    }

    public List<string> GetTags(string folderName)
    {
        lock (_lock) return _data.TryGetValue(folderName, out var tags) ? [.. tags] : [];
    }

    public void Flush() => _saveService.Flush();

    public void SetTags(string folderName, IReadOnlyList<string> tags)
    {
        lock (_lock)
        {
            if (tags.Count == 0)
                _data.Remove(folderName);
            else
                _data[folderName] = [.. tags];
        }
        Save();
    }

    private void Save()
    {
        try
        {
            Dictionary<string, List<string>> snapshot;
            lock (_lock) snapshot = new Dictionary<string, List<string>>(_data);
            string json = JsonSerializer.Serialize(snapshot, JsonSettings.DefaultOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to serialise tags", ex);
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (data != null) _data = data;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load tags", ex);
            AtomicFile.TryBackupCorruptedFile(_path);
        }
    }
}
