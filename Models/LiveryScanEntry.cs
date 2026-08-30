namespace LiveryGallery.Models;

internal record LiveryScanEntry
{
    public List<LiveryEntry> Entries { get; set; } = [];
    public int ReusedFromCache { get; set; }
    public int Parsed {  get; set; }
    public int Errors { get; set; }
    public int Removed {  get; set; }
}
