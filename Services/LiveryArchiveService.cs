using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Text.Json;

namespace LiveryGallery.Services;

internal sealed class LiveryArchiveService
{
    private static readonly string _indexPath = Path.Combine(AppSettings.ArchivePath, "archive_index.json");
    private readonly SaveFolderLock _saveFolderLock;
    private readonly Lock _lock = new();
    private Dictionary<string, ArchivedLiveryEntry> _index = [];

    public LiveryArchiveService(SaveFolderLock saveFolderLock)
    {
        _saveFolderLock = saveFolderLock;
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_indexPath))
            {
                string json = File.ReadAllText(_indexPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, ArchivedLiveryEntry>>(json);
                if (data is not null)
                {
                    _index = data;
                    ReconcileWithDisk();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load archive index", ex);
            AtomicFile.TryBackupCorruptedFile(_indexPath);
        }

        _index = [];
    }

    private void ReconcileWithDisk()
    {
        var toRemove = new List<string>();
        foreach (var (folderName, _) in _index)
        {
            if (!TryResolveArchiveFolderPath(folderName, out string resolvedPath) || !Directory.Exists(resolvedPath))
                toRemove.Add(folderName);
        }
        foreach (var folderName in toRemove) _index.Remove(folderName);
        if (toRemove.Count > 0)
        {
            AppLogger.LogError(
                $"Archive index referenced {toRemove.Count} folder(s) that no longer exist on disk " +
                $"({string.Join(", ", toRemove)}) — removed from the index",
                new InvalidOperationException("Archive index/disk mismatch"));
        }

        try
        {
            var indexed = _index.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphanedOnDisk = Directory.EnumerateDirectories(AppSettings.ArchivePath)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !indexed.Contains(name) && IsValidFolderName(name))
                .ToList();

            if (orphanedOnDisk.Count > 0)
            {
                foreach (var folderName in orphanedOnDisk)
                {
                    DateTime archivedAtUtc;
                    try { archivedAtUtc = Directory.GetCreationTimeUtc(Path.Combine(AppSettings.ArchivePath, folderName)); }
                    catch { archivedAtUtc = DateTime.UtcNow; }

                    _index[folderName] = new ArchivedLiveryEntry
                    {
                        ArchivedAtUtc = archivedAtUtc,
                        Data = new LiveryData
                        {
                            FolderName = folderName,
                            LiveryName = folderName,
                            AuthorRaw = Strings.ArchiveRecoveredUnknownValue,
                            CarId = 0,
                            CarManufacturerRaw = Strings.ArchiveRecoveredUnknownValue,
                            CarModelNameRaw = "",
                            CarKnown = false,
                            HasThumbnail = false,
                        }
                    };
                }

                AppLogger.LogError(
                    $"{orphanedOnDisk.Count} folder(s) existed in the archive but weren't in the index " +
                    $"({string.Join(", ", orphanedOnDisk)}) — recovered with placeholder metadata " +
                    "(folder name shown as title, author/car unknown) so they remain accessible",
                    new InvalidOperationException("Archive index/disk mismatch"));

                _ = SaveAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to enumerate archive folders for reconciliation", ex);
        }
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            Dictionary<string, ArchivedLiveryEntry> snapshot;
            lock (_lock) snapshot = new(_index);
            string json = JsonSerializer.Serialize(snapshot, JsonSettings.DefaultOptions);
            return await PersistenceManager.SaveNowAsync(_indexPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save archive index", ex);
            return false;
        }
    }

    public IReadOnlyList<ArchivedLiveryEntry> GetAll()
    {
        lock (_lock) return [.. _index.Values.OrderByDescending(e => e.ArchivedAtUtc)];
    }

    public HashSet<string> GetReferencedThumbnailFiles()
    {
        lock (_lock)
        {
            return [.. _index.Values
                .Select(e => e.Data.ThumbnailPath)
                .OfType<string>()
                .Select(Path.GetFileName)
                .OfType<string>()];
        }
    }

    public async Task<List<string>> MoveToArchiveAsync(IEnumerable<LiveryData> entries, string savePath)
    {
        using var _ = await _saveFolderLock.AcquireAsync();

        var moved = new List<(string FolderName, ArchivedLiveryEntry Entry)>();

        foreach (var data in entries)
        {
            if (!TryResolveArchiveFolderPath(data.FolderName, out string destination)) continue;
            string source = Path.Combine(savePath, data.FolderName);
            var outcome = TryMoveDirectory(source, destination);
            if (outcome != MoveOutcome.Moved) continue;
            moved.Add((data.FolderName, new ArchivedLiveryEntry { Data = data, ArchivedAtUtc = DateTime.UtcNow }));
        }

        if (moved.Count > 0)
        {
            lock (_lock)
                foreach (var (folderName, entry) in moved)
                    _index[folderName] = entry;

            if (!await SaveAsync())
            {
                AppLogger.LogError(
                    $"Moved {moved.Count} folder(s) to archive on disk, but failed to persist the archive " +
                    "index — they'll still show in this session, but may not survive an app restart until " +
                    "the index saves successfully",
                    new InvalidOperationException("Archive index persistence failed after successful move"));
            }
        }

        return [.. moved.Select(m => m.FolderName)];
    }

    public async Task<List<string>> RestoreAsync(IEnumerable<string> folderNames, string savePath)
    {
        using var _ = await _saveFolderLock.AcquireAsync();

        var restored = new List<string>();

        foreach (var folderName in folderNames)
        {
            if (!TryResolveArchiveFolderPath(folderName, out string source)) continue;
            string destination = Path.Combine(savePath, folderName);
            var outcome = TryMoveDirectory(source, destination);
            if (outcome != MoveOutcome.Moved) continue;

            restored.Add(folderName);
        }

        if (restored.Count > 0)
        {
            lock (_lock)
                foreach (var folderName in restored)
                    _index.Remove(folderName);

            if (!await SaveAsync())
            {
                AppLogger.LogError(
                    $"Restored {restored.Count} folder(s) from archive on disk, but failed to persist the " +
                    "archive index — a future restart will self-correct via reconciliation",
                    new InvalidOperationException("Archive index persistence failed after successful restore"));
            }
        }

        return restored;
    }

    public async Task<List<string>> DeletePermanentlyAsync(IEnumerable<string> folderNames)
    {
        var deleted = new List<string>();

        foreach (var folderName in folderNames)
        {
            if (!TryResolveArchiveFolderPath(folderName, out string path)) continue;
            if (!Directory.Exists(path))
            {
                deleted.Add(folderName);
                continue;
            }

            if (RecycleBinHelper.TrySendToRecycleBin(path)) deleted.Add(folderName);
        }

        if (deleted.Count > 0)
        {
            lock (_lock)
                foreach (var folderName in deleted)
                    _index.Remove(folderName);

            if (!await SaveAsync())
            {
                AppLogger.LogError(
                    $"Deleted {deleted.Count} folder(s) from archive on disk, but failed to persist the " +
                    "archive index — a future restart will self-correct via reconciliation",
                    new InvalidOperationException("Archive index persistence failed after successful delete"));
            }
        }

        return deleted;
    }

    private static bool IsValidFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (Path.IsPathRooted(name)) return false;
        if (name is "." or "..") return false;
        if (name != Path.GetFileName(name)) return false;
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar)) return false;
        return true;
    }

    private static bool TryResolveArchiveFolderPath(string folderName, out string path)
    {
        path = "";
        if (!IsValidFolderName(folderName)) return false;

        string archiveRoot = Path.GetFullPath(AppSettings.ArchivePath);
        string candidate = Path.GetFullPath(Path.Combine(archiveRoot, folderName));
        string archiveRootWithSeparator = archiveRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(archiveRootWithSeparator, StringComparison.OrdinalIgnoreCase)) return false;

        path = candidate;
        return true;
    }

    private static MoveOutcome TryMoveDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return MoveOutcome.Failed;
        if (Directory.Exists(destination))
        {
            AppLogger.LogError($"Cannot move '{source}' to '{destination}' — destination already exists",
                new IOException("Destination already exists"));
            return MoveOutcome.Failed;
        }

        try
        {
            Directory.Move(source, destination);
            return MoveOutcome.Moved;
        }
        catch (IOException)
        {
            try
            {
                CopyDirectoryRecursive(source, destination);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to copy '{source}' to '{destination}'", ex);
                try 
                { 
                    if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); 
                }
                catch 
                {

                }
                return MoveOutcome.Failed;
            }

            try
            {
                Directory.Delete(source, recursive: true);
                return MoveOutcome.Moved;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(
                    $"Copied '{source}' to '{destination}' successfully, but failed to delete the source " +
                    "afterward — both copies now exist on disk; keeping both rather than risking deletion " +
                    "of the only complete copy", ex);
                return MoveOutcome.CopiedButSourceRemains;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to move '{source}' to '{destination}'", ex);
            return MoveOutcome.Failed;
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
