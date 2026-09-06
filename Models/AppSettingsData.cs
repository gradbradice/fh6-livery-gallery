using LiveryGallery.Enums;

namespace LiveryGallery.Models;

internal class AppSettingsData
{
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public string? SavePath { get; set; }
    public bool DarkTheme { get; set; }
    public SortMode SortMode { get; set; } = SortMode.Manufacture;
    public FavoriteMode FavoriteMode { get; set; } = FavoriteMode.None;
    public bool SeparateFavorites { get; set; } = false;
    public DuplicatesFilterMode DuplicatesFilterMode { get; set; } = DuplicatesFilterMode.All;
    public bool GroupingEnabled { get; set; } = true;
    public string? GameInstallPath { get; set; }
}
