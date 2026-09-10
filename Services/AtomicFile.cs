namespace LiveryGallery.Services;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        string tmpPath = path + ".tmp";
        try
        {
            File.WriteAllText(tmpPath, content);
            Replace(tmpPath, path);
        }
        finally
        {
            TryDeleteTempFile(tmpPath);
        }
    }

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        string tmpPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, content, ct);
            Replace(tmpPath, path);
        }
        finally
        {
            TryDeleteTempFile(tmpPath);
        }
    }

    private static void TryDeleteTempFile(string tmpPath)
    {
        try
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to delete temporary file '{tmpPath}'", ex);
        }
    }

    private static void Replace(string tmpPath, string path)
    {
        if (File.Exists(path))
            File.Replace(tmpPath, path, destinationBackupFileName: null);
        else
            File.Move(tmpPath, path, overwrite: true);
    }

    public static void TryBackupCorruptedFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Copy(path, path + ".corrupted", overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to create a backup copy of the corrupted file '{path}'", ex);
        }
    }
}
