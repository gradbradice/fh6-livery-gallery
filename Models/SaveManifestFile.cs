namespace LiveryGallery.Models;

internal sealed class SaveManifestFile
{
    public SaveManifestData? Manifest { get; set; }
}

internal sealed class SaveManifestData
{
    public string? UserId { get; set; }
    /// <summary>hex 16D460</summary>
    public string? GameId { get; set; }
}
