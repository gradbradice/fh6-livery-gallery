using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

internal sealed class BackupRowViewModel(
    LiveryBackupSummary summary, IReadOnlyList<LiveryData> currentEntries, AccountContext accounts)
{
    public string Path { get; } = summary.Path;
    public LiveryBackupManifest? Manifest { get; } = summary.Manifest;
    public AccountContext Accounts { get; } = accounts;

    /// <summary>Made on another account: its comparison with the open account would only list differences</summary>
    public bool IsFromOtherAccount { get; } = accounts.IsOther(summary.Manifest?.SourceAccount);

    public string OtherAccountText { get; } = accounts.SourceNote(summary.Manifest?.SourceAccount);

    /// <summary>Hidden by "only this account": another account that still exists</summary>
    public bool IsHiddenByAccountFilter { get; } = accounts.IsHiddenByAccountFilter(summary.Manifest?.SourceAccount);

    public string DateText => Manifest is not null
        ? Manifest.CreatedAtUtc.ToLocalTime().ToString("g")
        : Strings.BackupUnknownDate;

    public string SizeText => ByteSizeFormatter.Format(summary.SizeBytes);

    public string EntryCountText => string.Format(Strings.BackupEntryCountFormat, Manifest?.Entries.Count ?? 0);

    public bool HasDiff => Manifest is not null && !IsFromOtherAccount;

    public string DiffText { get; } = BuildDiffText(summary.Manifest, currentEntries);

    private static string BuildDiffText(LiveryBackupManifest? manifest, IReadOnlyList<LiveryData> currentEntries)
    {
        if (manifest is null) return "";
        var (added, removed) = LiveryBackups.ComputeDiff(manifest, currentEntries);
        return string.Format(Strings.BackupDiffFormat, added.Count, removed.Count);
    }
}
