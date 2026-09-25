using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed class BackupRowViewModel(LiveryBackupService.BackupSummary summary, IReadOnlyList<LiveryData> currentEntries)
{
    public string Path { get; } = summary.Path;
    public LiveryBackupManifest? Manifest { get; } = summary.Manifest;

    public string DateText => Manifest is not null
        ? Manifest.CreatedAtUtc.ToLocalTime().ToString("g")
        : Strings.BackupUnknownDate;

    public string SizeText => FormatSize(summary.SizeBytes);

    public string EntryCountText => string.Format(Strings.BackupEntryCountFormat, Manifest?.Entries.Count ?? 0);

    public bool HasDiff => Manifest is not null;

    public string DiffText
    {
        get
        {
            if (Manifest is null) return "";
            var (added, removed) = LiveryBackupService.ComputeDiff(Manifest, currentEntries);
            return string.Format(Strings.BackupDiffFormat, added.Count, removed.Count);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.#} {units[unitIndex]}";
    }
}
