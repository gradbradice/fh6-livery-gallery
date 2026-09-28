using LiveryGallery.Configuration;
using System.Text.Json;

namespace LiveryGallery.Services;

internal sealed class InstalledLiveriesCache
{
    public const int CurrentVersion = 1;

    public static readonly TimeSpan MaxResultAge = TimeSpan.FromDays(7);

    private readonly string _path;

    public InstalledLiveriesCache(string? path = null) =>
        _path = path ?? Path.Combine(AppSettings.BaseCachePath, "installed-liveries.json");

    internal sealed record Snapshot
    {
        public int Version { get; init; } = CurrentVersion;
        public string? ProfilePath { get; init; }
        public string? Hash { get; init; }
        public Dictionary<string, int>? Installed { get; init; }
        public DateTime? ResultAtUtc { get; init; }
        public string? RejectedHash { get; init; }
        public DateTime? LastUploadUtc { get; init; }
    }

    public Snapshot? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path));
            return snapshot?.Version == CurrentVersion ? snapshot : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_path, $"Failed to read '{_path}' — the save will be sent again once", ex);
            return null;
        }
    }

    public void Save(Snapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_path, $"Failed to write '{_path}'", ex);
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(_path, $"Failed to delete '{_path}'", ex);
        }
    }

    public static bool IsResultUsable(Snapshot? snapshot, string? profilePath, DateTime utcNow) =>
        snapshot is { Hash: not null, Installed: not null, ResultAtUtc: { } at }
        && utcNow - at < MaxResultAge
        && at <= utcNow + TimeSpan.FromMinutes(5)
        && string.Equals(snapshot.ProfilePath, profilePath, StringComparison.OrdinalIgnoreCase);
}
