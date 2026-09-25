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
    public bool ShowEmptyState => Backups.Count == 0;

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
        Reload();
    }

    private void Reload()
    {
        var currentEntries = _getCurrentEntries();
        Backups.Clear();
        foreach (var summary in _backupService.GetAllBackups())
            Backups.Add(new BackupRowViewModel(summary, currentEntries));
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
            Reload();
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
        if (LiveryBackupService.TryDeleteBackup(row.Path)) Reload();
    }
}
