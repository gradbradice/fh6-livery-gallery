using Avalonia.Media.Imaging;

namespace LiveryGallery.Services;

internal static class ThumbnailService
{
    private static readonly SemaphoreSlim _generationLimiter = new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>
    /// The PNG name: the livery folder and the hash of the game's preview. The same picture under the same folder name
    /// is the same PNG, wherever the save folder is: moving the save (another drive, Steam ↔ Xbox app) does not make
    /// new copies. Before 1.4.1 the name also had a hash of the full folder path; such names stay valid until the
    /// livery changes.
    /// </summary>
    public static string ComputeDestinationFileName(string folderName, string? sourceThumbHash)
    {
        string hashSuffix = sourceThumbHash is { Length: >= 12 } hash
            ? hash[..12]
            : sourceThumbHash ?? Guid.NewGuid().ToString("N")[..12];
        return SanitiseFileName(folderName) + "_" + hashSuffix + ".png";
    }

    public static bool GenerateAndSave(string sourceWebpPath, string destPngPath, int maxWidth = 360)
    {
        try
        {
            using var srcStream = File.OpenRead(sourceWebpPath);
            return GenerateAndSave(srcStream, sourceWebpPath, destPngPath, maxWidth);
        }
        catch (Exception ex)
        {
            AppLog.LogErrorThrottled(sourceWebpPath, $"Failed to generate thumbnail from '{sourceWebpPath}'", ex);
            return false;
        }
    }

    public static bool GenerateAndSave(Stream srcStream, string sourceDescription, string destPngPath, int maxWidth = 360)
    {
        _generationLimiter.Wait();
        try
        {
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
            AppLog.LogErrorThrottled(sourceDescription, $"Failed to generate thumbnail from '{sourceDescription}'", ex);
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
}
