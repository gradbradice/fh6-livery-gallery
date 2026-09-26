using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.IO.Compression;
using System.Text.Json;

namespace LiveryGallery.Services;

internal sealed class LiveryBackupService(SaveFolderLock saveFolderLock)
{
    private const string ManifestEntryName = "manifest.json";
    private const string LiveriesPrefix = "liveries/";

    public async Task<string> CreateBackupAsync(IReadOnlyList<LiveryData> entries, string savePath)
    {
        using var _ = await saveFolderLock.AcquireAsync();
        string baseFileName = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string finalPath = Path.Combine(AppSettings.BackupsPath, $"{baseFileName}.liverybackup");
        for (int suffix = 2; File.Exists(finalPath); suffix++)
            finalPath = Path.Combine(AppSettings.BackupsPath, $"{baseFileName}_{suffix}.liverybackup");
        string tempPath = finalPath + ".tmp";

        await Task.Run(() =>
        {
            try
            {
                var existingEntries = entries.Where(e => Directory.Exists(Path.Combine(savePath, e.FolderName))).ToList();

                using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    var portableEntries = existingEntries
                        .Select(e => e with { ThumbnailPath = Path.GetFileName(e.ThumbnailPath) })
                        .ToList();
                    var manifest = new LiveryBackupManifest { CreatedAtUtc = DateTime.UtcNow, Entries = portableEntries };
                    var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(manifestEntry.Open()))
                        writer.Write(JsonSerializer.Serialize(manifest, JsonSettings.DefaultOptions));

                    foreach (var data in existingEntries)
                    {
                        string folderPath = Path.Combine(savePath, data.FolderName);
                        foreach (string file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                        {
                            string relative = Path.GetRelativePath(folderPath, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, $"{LiveriesPrefix}{data.FolderName}/{relative}", CompressionLevel.Optimal);
                        }
                    }
                }

                File.Move(tempPath, finalPath);
            }
            catch
            {
                try 
                { 
                    if (File.Exists(tempPath)) File.Delete(tempPath); 
                }
                catch 
                {

                }
                throw;
            }
        });

        return finalPath;
    }

    public readonly record struct BackupSummary(string Path, long SizeBytes, LiveryBackupManifest? Manifest);

    public IReadOnlyList<BackupSummary> GetAllBackups()
    {
        var result = new List<BackupSummary>();
        foreach (string file in Directory.EnumerateFiles(AppSettings.BackupsPath, "*.liverybackup"))
        {
            long size;
            try { size = new FileInfo(file).Length; }
            catch (Exception ex) { AppLogger.LogError($"Failed to read size of '{file}'", ex); continue; }

            result.Add(new BackupSummary(file, size, TryReadManifest(file)));
        }

        return [.. result.OrderByDescending(b => b.Manifest?.CreatedAtUtc ?? DateTime.MinValue)];
    }

    private static LiveryBackupManifest? TryReadManifest(string backupPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(backupPath);
            var entry = zip.GetEntry(ManifestEntryName);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<LiveryBackupManifest>(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to read backup manifest from '{backupPath}'", ex);
            return null;
        }
    }

    public static (List<LiveryData> Added, List<LiveryData> Removed) ComputeDiff(
        LiveryBackupManifest manifest, IReadOnlyList<LiveryData> currentEntries)
    {
        var backupNames = manifest.Entries.Select(e => e.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentNames = currentEntries.Select(e => e.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = currentEntries.Where(e => !backupNames.Contains(e.FolderName)).ToList();
        var removed = manifest.Entries.Where(e => !currentNames.Contains(e.FolderName)).ToList();
        return (added, removed);
    }

    private const long MaxSingleFileBytes = 10L * 1024 * 1024;
    private const long MaxTotalUncompressedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntryCount = 5000;

    public async Task<List<string>> RestoreEntriesAsync(string backupPath, IEnumerable<string> folderNames, string savePath)
    {
        using var _ = await saveFolderLock.AcquireAsync();

        var wanted = folderNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restored = new List<string>();

        await Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(backupPath);
            var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var started = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytesSoFar = 0;
            int entryCountSoFar = 0;

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(LiveriesPrefix, StringComparison.Ordinal)) continue;

                string relative = entry.FullName[LiveriesPrefix.Length..];
                int slashIdx = relative.IndexOf('/');
                if (slashIdx < 0) continue;

                string folderName = relative[..slashIdx];
                if (!wanted.Contains(folderName) || skipped.Contains(folderName)) continue;

                if (entry.Length > MaxSingleFileBytes)
                {
                    AppLogger.LogError(
                        $"Refusing to restore '{folderName}' — entry '{entry.FullName}' is {entry.Length:N0} bytes " +
                        $"uncompressed, exceeding the {MaxSingleFileBytes:N0} byte per-file limit",
                        new InvalidOperationException("Backup entry exceeds size limit (possible zip bomb)"));
                    skipped.Add(folderName);
                    continue;
                }
                if (++entryCountSoFar > MaxEntryCount || (totalBytesSoFar += entry.Length) > MaxTotalUncompressedBytes)
                {
                    AppLogger.LogError(
                        $"Refusing to continue restoring — exceeded overall limits ({MaxEntryCount:N0} files / " +
                        $"{MaxTotalUncompressedBytes:N0} bytes uncompressed) for this restore operation",
                        new InvalidOperationException("Backup restore exceeds overall size limits (possible zip bomb)"));
                    break;
                }

                if (folderName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(folderName))
                {
                    AppLogger.LogError($"Refusing to restore '{folderName}' — suspicious folder name in backup archive",
                        new InvalidOperationException("Potential path traversal in backup entry"));
                    skipped.Add(folderName);
                    continue;
                }

                string destinationFolder = Path.Combine(savePath, folderName);
                string destinationFolderFull = Path.GetFullPath(destinationFolder);
                string destinationFolderRoot = destinationFolderFull + Path.DirectorySeparatorChar;
                string savePathRoot = Path.GetFullPath(savePath) + Path.DirectorySeparatorChar;
                if (!destinationFolderRoot.StartsWith(savePathRoot, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogError($"Refusing to restore '{folderName}' — resolved path escapes the save folder",
                        new InvalidOperationException("Potential path traversal in backup entry"));
                    skipped.Add(folderName);
                    continue;
                }

                try
                {
                    if (!started.Contains(folderName))
                    {
                        if (Directory.Exists(destinationFolder))
                        {
                            AppLogger.LogError($"Cannot restore '{folderName}' — already exists at '{destinationFolder}'",
                                new IOException("Destination already exists"));
                            skipped.Add(folderName);
                            continue;
                        }
                        Directory.CreateDirectory(destinationFolder);
                        started.Add(folderName);
                    }

                    string relativeInFolder = relative[(slashIdx + 1)..].Replace('/', Path.DirectorySeparatorChar);
                    string destinationFile = Path.GetFullPath(Path.Combine(destinationFolder, relativeInFolder));

                    if (!destinationFile.StartsWith(destinationFolderRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.LogError(
                            $"Refusing to extract entry '{entry.FullName}' — resolved path '{destinationFile}' " +
                            $"escapes the destination folder '{destinationFolder}'",
                            new InvalidOperationException("Potential path traversal (Zip Slip) in backup entry"));
                        continue;
                    }

                    string? destDir = Path.GetDirectoryName(destinationFile);
                    if (destDir is not null) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destinationFile, overwrite: false);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Failed to restore '{folderName}' from '{backupPath}'", ex);
                    skipped.Add(folderName);
                    try 
                    { 
                        if (Directory.Exists(destinationFolder)) Directory.Delete(destinationFolder, recursive: true); 
                    }
                    catch 
                    {

                    }
                }
            }

            restored.AddRange(started.Except(skipped));
        });

        return restored;
    }

    public static bool TryDeleteBackup(string backupPath) => RecycleBinHelper.TrySendToRecycleBin(backupPath);

    public static string? FindExistingPreview(string? thumbnailFileName)
    {
        if (!IsSafePreviewFileName(thumbnailFileName)) return null;

        string cached = Path.Combine(AppSettings.ThumbsPath, thumbnailFileName!);
        if (File.Exists(cached)) return cached;

        string backupPreview = Path.Combine(AppSettings.BackupThumbsPath, thumbnailFileName!);
        return File.Exists(backupPreview) ? backupPreview : null;
    }

    public static Task GenerateMissingPreviewsAsync(
        string backupPath, IReadOnlyList<LiveryData> entries, IProgress<(string FolderName, string PreviewPath)> progress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                using var zip = ZipFile.OpenRead(backupPath);
                foreach (var data in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsSafePreviewFileName(data.ThumbnailPath)) continue;

                    var source = zip.GetEntry($"{LiveriesPrefix}{data.FolderName}/bigThumb.webp")
                        ?? zip.GetEntry($"{LiveriesPrefix}{data.FolderName}/thumb.webp");
                    if (source is null || source.Length > MaxSingleFileBytes) continue;
                    using var buffer = new MemoryStream((int)source.Length);
                    using (var entryStream = source.Open()) entryStream.CopyTo(buffer);
                    buffer.Position = 0;

                    string destination = Path.Combine(AppSettings.BackupThumbsPath, data.ThumbnailPath!);
                    if (ThumbnailService.GenerateAndSave(buffer, $"{backupPath}:{source.FullName}", destination))
                        progress.Report((data.FolderName, destination));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to generate previews from backup '{backupPath}'", ex);
            }
        }, CancellationToken.None);
    }

    public static void PruneBackupPreviews(IReadOnlyList<BackupSummary> backups)
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
                catch (Exception ex) { AppLogger.LogErrorThrottled(file, $"Failed to delete unused backup preview '{file}'", ex); }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to prune backup previews", ex);
        }
    }

    private static bool IsSafePreviewFileName(string? fileName) =>
        !string.IsNullOrEmpty(fileName)
        && fileName == Path.GetFileName(fileName)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
}
