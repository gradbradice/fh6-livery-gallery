using ForzaToolkit.Liveries;
using ForzaToolkit.Liveries.Storage;
using ForzaToolkit.Saves.Accounts;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

/// <summary>When the gallery thinks of a backup of its own</summary>
internal enum AutoBackupMoment
{
    /// <summary>Liveries are about to go to the archive</summary>
    BeforeArchive,

    /// <summary>A scan has finished: the save may have changed, or a scheduled backup may be due</summary>
    AfterScan,
}

/// <summary>The settings of automatic backups, as the policy reads them</summary>
internal sealed record AutoBackupSettings(bool BeforeArchive, bool OnChange, bool Periodic, int PeriodDays, int Keep)
{
    public static AutoBackupSettings From(AppSettingsData settings) => new(
        settings.AutoBackupBeforeArchive, settings.AutoBackupOnChange, settings.AutoBackupPeriodic,
        AutoBackupPolicy.ClampPeriodDays(settings.AutoBackupPeriodDays), AutoBackupPolicy.ClampKeep(settings.AutoBackupKeep));
}

/// <summary>Whether a backup of the gallery's own is due, and of which kind. Pure</summary>
internal static class AutoBackupPolicy
{
    public const int DefaultPeriodDays = 7;
    public const int MinPeriodDays = 1;
    public const int MaxPeriodDays = 30;
    public const int DefaultKeep = 5;
    public const int MinKeep = 1;
    public const int MaxKeep = 20;

    /// <summary>A change of the save makes a backup at most this often: a day of downloads is one backup</summary>
    public static readonly TimeSpan ChangeInterval = TimeSpan.FromDays(1);

    public static int ClampPeriodDays(int days) => Math.Clamp(days, MinPeriodDays, MaxPeriodDays);

    public static int ClampKeep(int keep) => Math.Clamp(keep, MinKeep, MaxKeep);

    /// <param name="moment">When the gallery asks</param>
    /// <param name="settings">What the user turned on</param>
    /// <param name="nowUtc">Now</param>
    /// <param name="backups">The account's backups, any order</param>
    /// <param name="newestHoldsTheSave">The newest of them holds the save as it is now: a new one would hold nothing more</param>
    /// <returns>The kind of backup to make; null when none is due</returns>
    public static LiveryBackupKind? Due(AutoBackupMoment moment, AutoBackupSettings settings, DateTime nowUtc,
        IReadOnlyCollection<LiveryBackupManifest> backups, bool newestHoldsTheSave)
    {
        if (newestHoldsTheSave) return null;
        if (moment == AutoBackupMoment.BeforeArchive) return settings.BeforeArchive ? LiveryBackupKind.BeforeArchive : null;

        var lastAutomatic = backups.Where(b => b.IsAutomatic).Select(b => (DateTime?)b.CreatedAtUtc).Max();
        if (settings.OnChange && (lastAutomatic is null || nowUtc - lastAutomatic >= ChangeInterval))
            return LiveryBackupKind.SaveChanged;
        var last = backups.Select(b => (DateTime?)b.CreatedAtUtc).Max();
        if (settings.Periodic && (last is null || nowUtc - last >= TimeSpan.FromDays(settings.PeriodDays)))
            return LiveryBackupKind.Scheduled;
        return null;
    }
}

/// <summary>What became of an automatic backup</summary>
internal enum AutoBackupOutcome
{
    /// <summary>None was due</summary>
    NotDue,

    /// <summary>Made, and the oldest automatic backups beyond the kept number removed</summary>
    Made,

    /// <summary>Due, but writing it failed (logged)</summary>
    Failed,
}

/// <summary>
/// The gallery's own backups: before liveries go to the archive, after the save changed (at most once a day), on a
/// schedule. A backup is made only when the newest one does not already hold the save as it is; each account keeps its
/// newest <see cref="AutoBackupSettings.Keep"/> automatic backups, the user's own are never removed
/// </summary>
internal sealed class AutoBackups(LiveryBackups backups, AppSettingsData settings)
{
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>Makes a backup when one is due</summary>
    /// <param name="moment">When the gallery asks</param>
    /// <param name="entries">The liveries in the save, as the gallery knows them</param>
    /// <param name="savePath">The save folder</param>
    /// <param name="account">The open account; its backups are compared, and its automatic ones pruned</param>
    public async Task<AutoBackupOutcome> RunAsync(AutoBackupMoment moment, IReadOnlyList<LiveryData> entries, string savePath, AccountRef? account)
    {
        var config = AutoBackupSettings.From(settings);
        if (moment == AutoBackupMoment.BeforeArchive ? !config.BeforeArchive : !config.OnChange && !config.Periodic)
            return AutoBackupOutcome.NotDue;
        if (entries.Count == 0) return AutoBackupOutcome.NotDue;

        // One at a time: a scan finishing while the backup before an archive is written must not make a second one
        await _one.WaitAsync().ConfigureAwait(true);
        try
        {
            bool Mine(LiveryBackupManifest m) => m.SourceAccount is null || account is null
                || string.Equals(m.SourceAccount.Key, account.Key, StringComparison.OrdinalIgnoreCase);
            var mine = await Task.Run(() => backups.GetAll().Select(b => b.Manifest).OfType<LiveryBackupManifest>().Where(Mine).ToList())
                .ConfigureAwait(true);
            var newest = mine.MaxBy(m => m.CreatedAtUtc);
            bool same = newest is not null && LiveryBackups.HoldsTheSame(newest, entries);
            if (AutoBackupPolicy.Due(moment, config, DateTime.UtcNow, mine, same) is not { } kind) return AutoBackupOutcome.NotDue;

            try
            {
                await backups.CreateAsync(entries, savePath, account, kind).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (backups.IsClosing)
            {
                return AutoBackupOutcome.NotDue; // the app is closing; the unfinished file was removed
            }
            catch (Exception ex)
            {
                AppLog.LogError($"Failed to make an automatic backup ({kind})", ex);
                return AutoBackupOutcome.Failed;
            }
            await Task.Run(() => backups.PruneAutomatic(config.Keep, Mine)).ConfigureAwait(true);
            return AutoBackupOutcome.Made;
        }
        finally
        {
            _one.Release();
        }
    }
}
