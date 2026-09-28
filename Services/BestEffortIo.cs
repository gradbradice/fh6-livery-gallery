namespace LiveryGallery.Services;

internal static class BestEffortIo
{
    public static bool TryDeleteFile(string path, string context)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(path, $"{context}: failed to delete '{path}'", ex);
            return false;
        }
    }

    public static bool TryDeleteDirectory(string path, string context)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(path, $"{context}: failed to delete '{path}'", ex);
            return false;
        }
    }
}
