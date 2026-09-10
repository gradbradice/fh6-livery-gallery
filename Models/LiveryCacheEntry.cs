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
    public string HeaderHash { get; set; } = string.Empty;
    public string SourceThumbHash { get; set; } = string.Empty;
    public string? CLiveryHash { get; set; }
    public long HeaderLength { get; set; }
    public DateTime HeaderLastWriteUtc { get; set; }
    public long SourceThumbLength { get; set; }
    public DateTime SourceThumbLastWriteUtc { get; set; }
    public long CLiveryLength { get; set; }
    public DateTime CLiveryLastWriteUtc { get; set; }

    public uint[]? SectionCounts { get; set; }
}