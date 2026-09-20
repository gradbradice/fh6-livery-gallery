using Avalonia.Media.Imaging;

namespace LiveryGallery.Services;

internal static class ThumbnailService
{
    private static readonly SemaphoreSlim _generationLimiter = new(Math.Max(2, Environment.ProcessorCount / 2));

    public static string? FindSourceThumbnail(string folderPath)
    {
        string big = Path.Combine(folderPath, "bigThumb.webp");
        if (File.Exists(big)) return big;
        string small = Path.Combine(folderPath, "thumb.webp");
        if (File.Exists(small)) return small;
        return null;
    }

    public static string ComputeDestinationFileName(string folderName, string folderPath, string? sourceThumbHash)
    {
        string hashSuffix = sourceThumbHash is { Length: >= 12 } hash
            ? hash[..12]
            : sourceThumbHash ?? Guid.NewGuid().ToString("N")[..12];
        return SanitiseFileName(folderName) + "_" + StableHash(folderPath) + "_" + hashSuffix + ".png";
    }

    public static bool GenerateAndSave(string sourceWebpPath, string destPngPath, int maxWidth = 360)
    {
        _generationLimiter.Wait();
        try
        {
            using var srcStream = File.OpenRead(sourceWebpPath);
            using var bitmap = Bitmap.DecodeToWidth(
                srcStream,
                maxWidth,
                BitmapInterpolationMode.MediumQuality);

            string? dir = Path.GetDirectoryName(destPngPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            AtomicFile.WriteViaStream(destPngPath, stream => bitmap.Save(stream, PngBitmapEncoderOptions.Default));
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(sourceWebpPath, $"Failed to generate thumbnail from '{sourceWebpPath}'", ex);
            return false;
        }
        finally
        {
            _generationLimiter.Release();
        }
    }

    private static string SanitiseFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static string StableHash(string input)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 1099511628211;
            }
            return hash.ToString("x16");
        }
    }
}
