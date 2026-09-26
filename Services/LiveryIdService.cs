using LiveryGallery.Configuration;
using System.Text.Json;

namespace LiveryGallery.Services;

internal sealed class LiveryIdService
{
    internal sealed class LiveryIdData
    {
        public ulong NextId { get; set; } = 1;
        public Dictionary<string, ulong> Ids { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "livery_ids.json");
    private static readonly string _backupPath = _path + ".bak";

    private readonly Lock _lock = new();
    private LiveryIdData _data;

    public LiveryIdService() => _data = Load();

    public ulong? TryGet(string folderName)
    {
        lock (_lock) return _data.Ids.TryGetValue(folderName, out ulong id) ? id : null;
    }

    public void EnsureAssigned(IEnumerable<(string FolderName, DateTime? DownloadDate)> folders)
    {
        string? json = null;
        lock (_lock)
        {
            var missing = folders
                .Where(f => !_data.Ids.ContainsKey(f.FolderName))
                .DistinctBy(f => f.FolderName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f.DownloadDate ?? DateTime.MaxValue)
                .ThenBy(f => f.FolderName, StringComparer.Ordinal)
                .ToList();
            if (missing.Count == 0) return;

            foreach (var f in missing)
                _data.Ids[f.FolderName] = _data.NextId++;

            json = JsonSerializer.Serialize(_data, JsonSettings.DefaultOptions);
        }

        Save(json);
    }

    private static void Save(string json)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.BaseCachePath);
            AtomicFile.WriteAllText(_path, json);
            AtomicFile.WriteAllText(_backupPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save livery IDs", ex);
        }
    }

    private static LiveryIdData Load()
    {
        var main = TryLoadFrom(_path);
        if (main is not null) return main;

        var backup = TryLoadFrom(_backupPath);
        if (backup is not null)
        {
            if (File.Exists(_path))
                AppLogger.LogError("livery_ids.json was unreadable — restored from livery_ids.json.bak",
                    new InvalidOperationException("Livery ID file recovered from backup"));
            return backup;
        }

        return new LiveryIdData();
    }

    private static LiveryIdData? TryLoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize<LiveryIdData>(File.ReadAllText(path));
            if (data?.Ids is null)
            {
                AtomicFile.TryBackupCorruptedFile(path);
                return null;
            }

            var ids = new Dictionary<string, ulong>(data.Ids, StringComparer.OrdinalIgnoreCase);
            ulong maxIssued = ids.Count > 0 ? ids.Values.Max() : 0;
            return new LiveryIdData { Ids = ids, NextId = Math.Max(data.NextId, maxIssued + 1) };
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to load '{path}'", ex);
            AtomicFile.TryBackupCorruptedFile(path);
            return null;
        }
    }
}
