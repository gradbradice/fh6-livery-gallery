namespace LiveryGallery.Models;

internal class CarDatabaseMetadata
{
    public string? ETag { get; set; }
    public string? ContentHash { get; set; }
    public int Count { get; set; }
    public DateTime LastCheckedUtc { get; set; }
}
