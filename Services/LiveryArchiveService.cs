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
        if (!Directory.Exists(AppSettings.ArchivePath))
        {
            AppLogger.LogError($"Archive folder '{AppSettings.ArchivePath}' not found — archive index left unchanged",
                new DirectoryNotFoundException(AppSettings.ArchivePath));
            return;
        }

        bool indexChanged = false;
        var toRemove = new List<string>();
        foreach (var (folderName, _) in _index)
        {
            if (!TryResolveArchiveFolderPath(folderName, out string resolvedPath) || !Directory.Exists(resolvedPath))
                toRemove.Add(folderName);
        }
        foreach (var folderName in toRemove) _index.Remove(folderName);
        if (toRemove.Count > 0)
        {
            indexChanged = true;
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
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        archivedAtUtc = DateTime.UtcNow; // only used for sorting
                    }

                    _index[folderName] = new ArchivedLiveryEntry
                    {
                        ArchivedAtUtc = archivedAtUtc,
                        Data = new LiveryData
                        {
                            FolderName = folderName,
                            LiveryName = folderName,
                            AuthorRaw = string.Empty, // shown as "Unknown" in the current language
                            TextVersion = LiveryCacheEntry.CurrentTextVersion,
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
                indexChanged = true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to enumerate archive folders for reconciliation", ex);
        }

        if (indexChanged) ScheduleSave();
    }

    private void ScheduleSave()
    {
        try
        {
            Dictionary<string, ArchivedLiveryEntry> snapshot;
            lock (_lock) snapshot = new(_index);
            PersistenceManager.Schedule(_indexPath, JsonSerializer.Serialize(snapshot, JsonSettings.DefaultOptions));
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save archive index", ex);
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

    public async Task<ArchiveOperationResult> MoveToArchiveAsync(IEnumerable<LiveryData> entries, string savePath)
    {
        using var _ = await _saveFolderLock.AcquireAsync();

        var moved = new List<(string FolderName, ArchivedLiveryEntry Entry)>();
        var leftovers = new List<string>();

        foreach (var data in entries)
        {
            if (_saveFolderLock.IsClosing) break;
            if (data.IsAuction)
            {
                AppLogger.LogError($"Refusing to archive auction livery '{data.FolderName}'",
                    new InvalidOperationException("Auction liveries can't be archived"));
                continue;
            }
            if (!TryResolveArchiveFolderPath(data.FolderName, out string destination)) continue;
            string source = Path.Combine(savePath, data.FolderName);
            var outcome = DirectoryMover.Move(source, destination, out string? leftover);
            if (leftover is not null) leftovers.Add(leftover);
            if (outcome == MoveOutcome.Failed) continue;
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

        return new ArchiveOperationResult([.. moved.Select(m => m.FolderName)], leftovers);
    }

    public async Task<ArchiveOperationResult> RestoreAsync(IEnumerable<string> folderNames, string savePath)
    {
        using var _ = await _saveFolderLock.AcquireAsync();

        var restored = new List<string>();
        var fullyRemovedFromArchive = new List<string>();
        var leftovers = new List<string>();

        foreach (var folderName in folderNames)
        {
            if (_saveFolderLock.IsClosing) break;
            if (!TryResolveArchiveFolderPath(folderName, out string source)) continue;
            string destination = Path.Combine(savePath, folderName);
            var outcome = DirectoryMover.Move(source, destination, out string? leftover);
            if (leftover is not null) leftovers.Add(leftover);
            if (outcome == MoveOutcome.Failed) continue;

            restored.Add(folderName);
            if (outcome == MoveOutcome.Moved) fullyRemovedFromArchive.Add(folderName);
        }

        if (fullyRemovedFromArchive.Count > 0)
        {
            lock (_lock)
                foreach (var folderName in fullyRemovedFromArchive)
                    _index.Remove(folderName);

            if (!await SaveAsync())
            {
                AppLogger.LogError(
                    $"Restored {restored.Count} folder(s) from archive on disk, but failed to persist the " +
                    "archive index — a future restart will self-correct via reconciliation",
                    new InvalidOperationException("Archive index persistence failed after successful restore"));
            }
        }

        return new ArchiveOperationResult(restored, leftovers);
    }

    public async Task<List<string>> DeletePermanentlyAsync(IEnumerable<string> folderNames)
    {
        using var _ = await _saveFolderLock.AcquireAsync();
        var deleted = new List<string>();

        foreach (var folderName in folderNames)
        {
            if (_saveFolderLock.IsClosing) break;
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
}
