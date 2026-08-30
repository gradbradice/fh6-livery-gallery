namespace LiveryGallery.Models;

internal record LiveryCacheEntry
{
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string LiveryName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int CarId { get; set; }
    public int CreatedYear { get; set; }
    public int CreatedMonth { get; set; }
    public DateTime? DownloadDate { get; set; }
    public string? PreviewFile { get; set; }
    public string? ThumbnailFile { get; set; }
    public DateTime FilesWruteUTC { get; set; }
}