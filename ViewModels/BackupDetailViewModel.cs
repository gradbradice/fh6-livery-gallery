using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class BackupDetailViewModel : ObservableObject
{
    private readonly LiveryBackupService _backupService;
    private readonly SavePathService _savePathService;
    private readonly string _backupPath;
    private readonly Func<Task> _onRestored;

    public string DateText { get; }
    public string SizeText { get; }

    public ObservableCollection<RemovedLiveryRowViewModel> RemovedRows { get; } = [];
    public ObservableCollection<LiveryData> AddedEntries { get; } = [];

    public bool ShowNoRemovedHint => RemovedRows.Count == 0;
    public bool ShowNoAddedHint => AddedEntries.Count == 0;
    public bool HasSelection => RemovedRows.Any(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, RemovedRows.Count(r => r.IsSelected));

    public event Action<string>? ErrorMessageRequested;

    public BackupDetailViewModel(
        LiveryBackupService backupService, SavePathService savePathService, BackupRowViewModel backup,
        IReadOnlyList<LiveryData> currentEntries, Func<Task> onRestored)
    {
        _backupService = backupService;
        _savePathService = savePathService;
        _backupPath = backup.Path;
        _onRestored = onRestored;
        DateText = backup.DateText;
        SizeText = backup.SizeText;

        var (added, removed) = LiveryBackupService.ComputeDiff(backup.Manifest!, currentEntries);

        foreach (var data in added.OrderBy(d => d.LiveryName, StringComparer.OrdinalIgnoreCase))
            AddedEntries.Add(data);

        foreach (var data in removed.OrderBy(d => d.LiveryName, StringComparer.OrdinalIgnoreCase))
        {
            var row = new RemovedLiveryRowViewModel(data);
            row.PropertyChanged += OnRowPropertyChanged;
            RemovedRows.Add(row);
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RemovedLiveryRowViewModel.IsSelected)) RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in RemovedRows) row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in RemovedRows) row.IsSelected = false;
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var selected = RemovedRows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        string? savePath = _savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return;
        }

        var folderNames = selected.Select(r => r.FolderName).ToList();
        try
        {
            var restored = await _backupService.RestoreEntriesAsync(_backupPath, folderNames, savePath);
            if (restored.Count == 0) return;

            var restoredSet = restored.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in RemovedRows.Where(r => restoredSet.Contains(r.FolderName)).ToList())
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                RemovedRows.Remove(row);
            }
            OnPropertyChanged(nameof(ShowNoRemovedHint));
            RaiseSelectionChanged();

            await _onRestored();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to restore from backup", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }
}
