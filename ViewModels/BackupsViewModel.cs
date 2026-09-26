using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class BackupsViewModel : ObservableObject
{
    private readonly LiveryBackupService _backupService;
    private readonly SavePathService _savePathService;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;

    public ObservableCollection<BackupRowViewModel> Backups { get; } = [];
    public bool ShowEmptyState => !_isLoading && Backups.Count == 0;

    private bool _isLoading;
    private int _reloadVersion;

    [ObservableProperty]
    private bool _isCreatingBackup;
    public event Action<string>? ErrorMessageRequested;
    public event Action<BackupRowViewModel>? OpenRequested;
    public event Action<BackupRowViewModel>? DeleteRequested;

    public BackupsViewModel(
        LiveryBackupService backupService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries)
    {
        _backupService = backupService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        int version = ++_reloadVersion;
        _isLoading = true;
        OnPropertyChanged(nameof(ShowEmptyState));

        var currentEntries = _getCurrentEntries();
        List<BackupRowViewModel> rows;
        try
        {
            rows = await Task.Run(() =>
            {
                var backups = _backupService.GetAllBackups();
                LiveryBackupService.PruneBackupPreviews(backups);
                return backups.Select(summary => new BackupRowViewModel(summary, currentEntries)).ToList();
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to list backups", ex);
            rows = [];
        }

        if (version != _reloadVersion) return; // a newer reload is in progress

        Backups.Clear();
        foreach (var row in rows) Backups.Add(row);
        _isLoading = false;
        OnPropertyChanged(nameof(ShowEmptyState));
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
            await _backupService.CreateBackupAsync(entries, savePath);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to create backup", ex);
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
        if (LiveryBackupService.TryDeleteBackup(row.Path)) _ = ReloadAsync();
    }
}
