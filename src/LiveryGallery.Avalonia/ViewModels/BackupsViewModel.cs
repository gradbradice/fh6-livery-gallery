using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class BackupsViewModel : ObservableObject
{
    private readonly LiveryBackups _backupService;
    private readonly SavePathService _savePathService;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly AccountContext _accounts;

    public ObservableCollection<BackupRowViewModel> Backups { get; } = [];
    public bool ShowEmptyState => !_isLoading && Backups.Count == 0;

    private List<BackupRowViewModel> _allRows = [];

    /// <summary>On by default: another account's backups are only shown when asked for</summary>
    [ObservableProperty]
    private bool _onlyCurrentAccount = true;

    public bool CanFilterByAccount => _allRows.Any(r => r.IsHiddenByAccountFilter);
    public int HiddenByAccountCount => OnlyCurrentAccount ? _allRows.Count(r => r.IsHiddenByAccountFilter) : 0;
    public bool HasHiddenByAccount => HiddenByAccountCount > 0;
    public string HiddenByAccountText => string.Format(Strings.AccountFilterHiddenFormat, HiddenByAccountCount);

    partial void OnOnlyCurrentAccountChanged(bool value) => ApplyAccountFilter();

    /// <summary>
    /// Backups of unknown origin (made before accounts were recorded) and of accounts that are gone are always shown
    /// </summary>
    private void ApplyAccountFilter()
    {
        Backups.Clear();
        foreach (var row in _allRows)
            if (!OnlyCurrentAccount || !row.IsHiddenByAccountFilter) Backups.Add(row);

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanFilterByAccount));
        OnPropertyChanged(nameof(HiddenByAccountCount));
        OnPropertyChanged(nameof(HasHiddenByAccount));
        OnPropertyChanged(nameof(HiddenByAccountText));
    }

    private bool _isLoading;
    private int _reloadVersion;
    private readonly CancellationTokenSource _lifetimeCts = new();

    [ObservableProperty]
    private bool _isCreatingBackup;
    public event Action<string>? ErrorMessageRequested;
    public event Action<BackupRowViewModel>? OpenRequested;
    public event Action<BackupRowViewModel>? DeleteRequested;

    public BackupsViewModel(
        LiveryBackups backupService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, AccountContext accounts)
    {
        _accounts = accounts;
        _backupService = backupService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        StartReload();
    }

    public void Close() => _lifetimeCts.Cancel();
    private void StartReload() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        var ct = _lifetimeCts.Token;
        if (ct.IsCancellationRequested) return;
        int version = ++_reloadVersion;
        _isLoading = true;
        OnPropertyChanged(nameof(ShowEmptyState));

        var currentEntries = _getCurrentEntries();
        List<BackupRowViewModel> rows;
        try
        {
            rows = await Task.Run(() =>
            {
                var backups = _backupService.GetAll();
                ct.ThrowIfCancellationRequested();
                BackupPreviews.Prune(backups);
                return backups.Select(summary => new BackupRowViewModel(summary, currentEntries, _accounts)).ToList();
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // dialog closed
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to list backups", ex);
            rows = [];
        }

        if (ct.IsCancellationRequested || version != _reloadVersion) return; // closed or a newer reload is in progress

        _allRows = rows;
        _isLoading = false;
        ApplyAccountFilter();
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        string? savePath = _savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return;
        }

        var entries = _getCurrentEntries();
        if (entries.Count == 0)
        {
            ErrorMessageRequested?.Invoke(Strings.BackupNothingToBackUpMessage);
            return;
        }

        IsCreatingBackup = true;
        try
        {
            await _backupService.CreateAsync(entries, savePath, _accounts.Current);
            await ReloadAsync();
        }
        catch (OperationCanceledException) when (_backupService.IsClosing)
        {
            // The app is closing. The unfinished backup file was removed, nothing to report
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to create backup", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
        finally
        {
            IsCreatingBackup = false;
        }
    }

    [RelayCommand]
    private void Open(BackupRowViewModel row) => OpenRequested?.Invoke(row);

    [RelayCommand]
    private void RequestDelete(BackupRowViewModel row) => DeleteRequested?.Invoke(row);

    public void DeleteConfirmed(BackupRowViewModel row)
    {
        if (LiveryBackups.TryDelete(row.Path)) StartReload();
    }
}
