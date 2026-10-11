using LiveryGallery.ViewModels;

namespace LiveryGallery.Models;

internal record LiveryScanEntry
{
    public List<LiveryEntry> Entries { get; set; } = [];
    public int ReusedFromCache { get; set; }
    public int Parsed {  get; set; }
    public int Errors { get; set; }
    public int Removed {  get; set; }
    public int Added { get; set; }
    public bool CacheChanged { get; set; }

    /// <summary>Only when the scan was asked to collect them; null when there is nothing meaningful to compare with</summary>
    public LiveryChanges? ChangesSinceLastLaunch { get; set; }
}
