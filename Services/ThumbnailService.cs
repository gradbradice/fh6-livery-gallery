using Avalonia.Media.Imaging;

namespace LiveryGallery.Services;

internal static class ThumbnailService
{
    public static string? FindSourceThumbnail(string folderPath)
    {
        string big = Path.Combine(folderPath, "bigThumb.webp");
        if (File.Exists(big)) return big;
        string small = Path.Combine(folderPath, "thumb.webp");
        if (File.Exists(small)) return small;
        return null;
    }

    public static bool GenerateAndSave(string sourceWebpPath, string destPngPath, int maxWidth = 360)
    {
        try
        {
            using var srcStream = File.OpenRead(sourceWebpPath);
            using var bitmap = Bitmap.DecodeToWidth(
                srcStream, 
                maxWidth, 
                BitmapInterpolationMode.MediumQuality);

            string? dir = Path.GetDirectoryName(destPngPath);
            if (dir is not null) Directory.CreateDirectory(dir);

            using var destStream = File.Create(destPngPath);
            bitmap.Save(destStream, PngBitmapEncoderOptions.Default);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
