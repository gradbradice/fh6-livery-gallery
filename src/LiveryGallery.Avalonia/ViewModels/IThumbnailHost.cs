using Avalonia.Media.Imaging;

namespace LiveryGallery.ViewModels;

internal interface IThumbnailHost
{
    string? ThumbnailPath { get; }
    Bitmap? Thumbnail { get; set; }
}
