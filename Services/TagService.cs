using LiveryGallery.Configuration;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class TagService
{
    private readonly SaveService _saveService;
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "tags.json");
    private static Dictionary<string, List<string>> _data = [];

    public TagService()
    {
        _saveService = new();
        Load();
    }

    public List<string> GetTags(string folderName) =>
        _data.TryGetValue(folderName, out var tags) ? [.. tags] : [];

    public void SetTags(string folderName, List<string> tags)
    {
        if (tags.Count == 0)
        {
            _data.Remove(folderName);
        }
        else
        {
            _data[folderName] = tags;
        }
        Save();
    }

    private void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_data, JsonSettings.DefaultDeserializeOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch
        {

        }
    }

    private static void Load()
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
        catch
        {

        }
    }
}
