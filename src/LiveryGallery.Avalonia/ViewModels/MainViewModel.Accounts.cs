using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

/// <summary>One entry of the account switcher</summary>
internal sealed record AccountOption(AccountInfo Info, string Title, string? Subtitle)
{
    public bool HasSubtitle => Subtitle is not null;

    /// <summary>The folder tells two unnamed accounts apart</summary>
    public string Tooltip => Info.SavePath;

    public static AccountOption From(AccountInfo info) => new(
        info,
        AccountLabels.Title(info.Key, info.DisplayName),
        info.ItemCount is { } count ? string.Format(Strings.AccountLiveriesFormat, count) : null);
}

/// <summary>
/// Several game accounts on one PC. Each has its own scan cache (see <see cref="AppCacheService"/>), so switching
/// only reads the other account's folder for changes, like a normal refresh, instead of parsing everything again.
/// Favorites, tags, livery IDs and author cards stay shared: they are keyed by livery folder name or by author,
/// which do not clash between accounts.
/// </summary>
internal sealed partial class MainViewModel
{
    private string? _appliedAccountKey;
    private bool _updatingAccounts;
    private int _accountsVersion;
    private Task? _accountSwitchTask;

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    /// <summary>The switcher is only shown when there is something to switch to</summary>
    public bool HasMultipleAccounts => Accounts.Count > 1;

    [ObservableProperty]
    private AccountOption? _selectedAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchAccount))]
    private bool _isSwitchingAccount;

    public bool CanSwitchAccount => !IsSwitchingAccount;

    private string? CurrentAccountKey => savePathService.SavePath is { } path ? AccountKey.For(path) : null;

    /// <summary>For the archive and backups: the open account, and the names of all known ones</summary>
    public AccountContext GetAccountContext()
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in Accounts) titles.TryAdd(option.Info.Key, option.Title);

        AccountRef? current = CurrentAccountKey is { } key
            ? new AccountRef(key, Accounts.FirstOrDefault(a => string.Equals(a.Info.Key, key, StringComparison.OrdinalIgnoreCase))?.Info.DisplayName)
            : null;
        // The switcher lists exactly the accounts whose save folder is on this PC (before its first refresh: unknown)
        var existing = Accounts.Count == 0 ? null : Accounts.Select(a => a.Info.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AccountContext(current, titles, existing);
    }

    /// <summary>For the settings: every account with data, how much it takes, and whether its save folder is gone</summary>
    public Task<List<StoredAccount>> ListStoredAccountsAsync()
    {
        string? currentKey = CurrentAccountKey;
        return Task.Run(() => _accountRegistry.ListStored(currentKey));
    }

    /// <summary>Deletes an account's data; the scan right after removes the previews only it used</summary>
    public async Task<bool> ForgetAccountAsync(string accountKey)
    {
        if (string.Equals(accountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase)) return false;
        if (!await _accountRegistry.ForgetAsync(accountKey)) return false;

        scanController.RequestOrphanCleanup();
        await RunScanAsyncTracked(isUserInitiated: false);
        await RefreshAccountsAsync();
        return true;
    }

    /// <summary>
    /// Once per launch, after the first scan: says in the status bar that accounts gone for a while still take
    /// space. Only a hint; nothing is deleted without the user (a folder may be on a drive that is just unplugged).
    /// </summary>
    private async Task PointOutGoneAccountsAsync()
    {
        List<StoredAccount> stored;
        try
        {
            stored = await ListStoredAccountsAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to list the stored accounts", ex);
            return;
        }

        var longGone = stored
            .Where(a => !a.IsCurrent && !a.SaveFolderExists
                && (a.GoneSinceUtc is not { } since || DateTime.UtcNow - since > AccountRegistry.ForgetSuggestionAge))
            .ToList();
        if (longGone.Count == 0) return;

        Status.SetStatusText(string.Format(
            Strings.StatusGoneAccountsFormat, longGone.Count, ByteSizeFormatter.Format(longGone.Sum(a => a.Bytes))));
    }

    partial void OnSelectedAccountChanged(AccountOption? value)
    {
        if (_updatingAccounts || value is null || AccountKey.SamePath(value.Info.SavePath, savePathService.SavePath)) return;
        _accountSwitchTask = RunSafelyAsync(() => SwitchAccountAsync(value.Info.SavePath), nameof(SwitchAccountAsync));
    }

    private async Task SwitchAccountAsync(string savePath)
    {
        IsSwitchingAccount = true;
        try
        {
            if (!await Task.Run(() => savePathService.Profile.Saves.IsValidSaveFolder(savePath)))
            {
                ErrorMessageRequested?.Invoke(Strings.AccountUnavailableMessage);
                await RefreshAccountsAsync();
                return;
            }

            savePathService.SetSavePath(savePath);
            await OnSavePathSwitchedAsync();
        }
        finally
        {
            IsSwitchingAccount = false;
        }
    }

    /// <summary>After any change of the save folder: the switcher, the settings, or the first-run prompt</summary>
    private async Task OnSavePathSwitchedAsync()
    {
        Gallery.ClearSelectionCommand.Execute(null);
        SavePathChanged?.Invoke();

        if (savePathService.ResolveSaveDataPath() is not null)
        {
            // An auto-refresh of the previous account may still be running: it is waited for, then this one scans
            for (int attempt = 0; attempt < 5 && !ShutdownToken.IsCancellationRequested; attempt++)
                if (await RunScanCoreAsync(isUserInitiated: true)) break;
        }
        await RefreshAccountsAsync();
    }

    public async Task RefreshAccountsAsync()
    {
        int version = ++_accountsVersion;
        string? currentSavePath = savePathService.SavePath;
        List<AccountInfo> infos;
        try
        {
            infos = await Task.Run(() => _accountRegistry.Discover(currentSavePath));
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to list the game accounts", ex);
            return;
        }
        if (version != _accountsVersion) return; // a newer refresh is on its way

        _updatingAccounts = true;
        try
        {
            Accounts.Clear();
            foreach (var info in infos) Accounts.Add(AccountOption.From(info));
            SelectedAccount = Accounts.FirstOrDefault(a => AccountKey.SamePath(a.Info.SavePath, savePathService.SavePath));
        }
        finally
        {
            _updatingAccounts = false;
        }
        OnPropertyChanged(nameof(HasMultipleAccounts));
    }

    /// <summary>
    /// Remembers what the switcher shows for this account. The name is the author of the account's own liveries
    /// (the MINE ones), which is the player's gamertag; an account without own liveries keeps a short ID.
    /// </summary>
    private void RecordCurrentAccount()
    {
        if (savePathService.SavePath is not { } savePath) return;

        string? displayName = Gallery.AllEntries
            .Where(e => e.IsMine && !e.Data.IsAuthorUnknown && !string.IsNullOrWhiteSpace(e.Data.AuthorRaw))
            .GroupBy(e => e.Data.AuthorRaw.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        ulong? userId = savePathService.CurrentUserId;
        int liveryCount = Gallery.AllEntries.Count;

        _ = RunSafelyAsync(async () =>
        {
            await Task.Run(() => _accountRegistry.Record(savePath, userId, displayName, liveryCount));
            await RefreshAccountsAsync();
        }, nameof(RecordCurrentAccount));
    }
}
