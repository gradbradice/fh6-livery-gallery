using System.IO.Compression;
using LiveryGallery.Configuration;

namespace LiveryGallery.Services;

/// <summary>
/// The gallery's PNG previews of liveries in backups: the backups themselves (ForzaToolkit.Liveries'
/// <see cref="LiveryBackups"/>) hold the game's WebP previews, which are turned into PNGs here when a backup is opened
/// </summary>
internal static class BackupPreviews
{
    /// <summary>The PNG of a livery in a backup: from the scan's preview cache, else made from the backup earlier</summary>
    public static string? FindExisting(string? thumbnailFileName)
    {
        if (!IsSafePreviewFileName(thumbnailFileName)) return null;

        string cached = Path.Combine(AppSettings.ThumbsPath, thumbnailFileName!);
        if (File.Exists(cached)) return cached;

        string backupPreview = Path.Combine(AppSettings.BackupThumbsPath, thumbnailFileName!);
        return File.Exists(backupPreview) ? backupPreview : null;
    }

    /// <summary>Makes the PNGs of <paramref name="entries"/> from the previews in the backup; reports each one made</summary>
    /// <param name="previewFileNames">The names of the game's previews of a livery, the better one first</param>
    public static Task GenerateMissingAsync(
        string backupPath, IReadOnlyList<LiveryData> entries, IReadOnlyList<string> previewFileNames,
        IProgress<(string FolderName, string PreviewPath)> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(backupPath);
            foreach (var data in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSafePreviewFileName(data.ThumbnailPath)) continue;

                var source = LiveryBackups.FindPreview(zip, data.FolderName, previewFileNames);
                if (source is null) continue;
                using var buffer = new MemoryStream((int)source.Length);
                using (var entryStream = source.Open()) entryStream.CopyTo(buffer);
                buffer.Position = 0;

                string destination = Path.Combine(AppSettings.BackupThumbsPath, data.ThumbnailPath!);
                if (ThumbnailService.GenerateAndSave(buffer, $"{backupPath}:{source.FullName}", destination))
                    progress.Report((data.FolderName, destination));
            }
        }, ct);
    }

    /// <summary>Removes PNGs no backup refers to any more; does nothing while a backup's manifest cannot be read</summary>
    public static void Prune(IReadOnlyList<LiveryBackupSummary> backups)
    {
        if (backups.Any(b => b.Manifest is null)) return;

        var referenced = backups
            .SelectMany(b => b.Manifest!.Entries)
            .Select(e => e.ThumbnailPath)
            .Where(IsSafePreviewFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string file in Directory.EnumerateFiles(AppSettings.BackupThumbsPath, "*.png"))
            {
                if (referenced.Contains(Path.GetFileName(file))) continue;
                try { File.Delete(file); }
                catch (Exception ex) { AppLog.LogErrorThrottled(file, $"Failed to delete unused backup preview '{file}'", ex); }
            }
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to prune backup previews", ex);
        }
    }

    private static bool IsSafePreviewFileName(string? fileName) =>
        !string.IsNullOrEmpty(fileName)
        && fileName == Path.GetFileName(fileName)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
}
