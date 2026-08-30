using LiveryGallery.Configuration;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class FavoriteService
{
    private readonly SaveService _saveService;
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "favorites.json");
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase); 

    public FavoriteService()
    {
        _saveService = new();
        Load();
    }

    public bool IsFavorite(string folderName) => _favorites.Contains(folderName);

    public void SetFavorite(string folderName, bool isFavorite)
    {
        _ = isFavorite ? _favorites.Add(folderName) : _favorites.Remove(folderName);
        Save();
    }

    private void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_favorites.ToList(), JsonSettings.DefaultDeserializeOptions);
            _saveService.ScheduleSave(json, _path);
        }
        catch
        {

        }
    }

    private void Load()
    {
        try
        {
            if(File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<List<string>>(json);
                if (data != null) _favorites = new HashSet<string>(data, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {

        }
    }
}
