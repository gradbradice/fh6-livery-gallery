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
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly CancellationTokenSource _previewCts = new();

    public string DateText { get; }
    public string SizeText { get; }

    public ObservableCollection<BackupLiveryRowViewModel> RemovedRows { get; } = [];
    public ObservableCollection<BackupLiveryRowViewModel> AddedEntries { get; } = [];

    public bool ShowNoRemovedHint => RemovedRows.Count == 0;
    public bool ShowNoAddedHint => AddedEntries.Count == 0;
    public bool HasSelection => RemovedRows.Any(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, RemovedRows.Count(r => r.IsSelected));

    public event Action<string>? ErrorMessageRequested;
    public Func<string, Task<bool>>? ConfirmRestoreDuplicatesAsync { get; set; }

    public BackupDetailViewModel(
        LiveryBackupService backupService, SavePathService savePathService, BackupRowViewModel backup,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored)
    {
        _backupService = backupService;
        _savePathService = savePathService;
        _backupPath = backup.Path;
        _onRestored = onRestored;
        _getCurrentEntries = getCurrentEntries;
        DateText = backup.DateText;
        SizeText = backup.SizeText;

        var (added, removed) = LiveryBackupService.ComputeDiff(backup.Manifest!, getCurrentEntries());

        foreach (var data in added.OrderBy(d => d.LiveryName, StringComparer.OrdinalIgnoreCase))
            AddedEntries.Add(BackupLiveryRowViewModel.FromCurrent(data));

        foreach (var data in removed.OrderBy(d => d.LiveryName, StringComparer.OrdinalIgnoreCase))
        {
            var row = BackupLiveryRowViewModel.FromBackup(data);
            row.PropertyChanged += OnRowPropertyChanged;
            RemovedRows.Add(row);
        }

        GenerateMissingPreviews();
    }

    public void CancelBackgroundWork() => _previewCts.Cancel();

    private void GenerateMissingPreviews()
    {
        var missing = RemovedRows.Where(r => r.NeedsPreview).Select(r => r.Data).ToList();
        if (missing.Count == 0) return;

        var progress = new Progress<(string FolderName, string PreviewPath)>(generated =>
        {
            if (_previewCts.IsCancellationRequested) return;
            ReplaceRowPreview(generated.FolderName, generated.PreviewPath);
        });
        _ = LiveryBackupService.GenerateMissingPreviewsAsync(_backupPath, missing, progress, _previewCts.Token);
    }

    private void ReplaceRowPreview(string folderName, string previewPath)
    {
        for (int i = 0; i < RemovedRows.Count; i++)
        {
            var old = RemovedRows[i];
            if (!string.Equals(old.FolderName, folderName, StringComparison.OrdinalIgnoreCase)) continue;

            var replacement = BackupLiveryRowViewModel.FromBackup(old.Data, previewPath);
            replacement.IsSelected = old.IsSelected;
            old.PropertyChanged -= OnRowPropertyChanged;
            replacement.PropertyChanged += OnRowPropertyChanged;
            RemovedRows[i] = replacement;
            return;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupLiveryRowViewModel.IsSelected)) RaiseSelectionChanged();
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

        try
        {
            var conflicts = LiveryRestoreConflicts.Find(
                selected.Select(r => r.Data), _getCurrentEntries(), savePath);

            var duplicateRows = selected.Where(r => conflicts.Duplicates.ContainsKey(r.FolderName)).ToList();
            bool restoreDuplicates = false;
            if (duplicateRows.Count > 0 && ConfirmRestoreDuplicatesAsync is { } confirm)
            {
                bool hasOtherLiveries = selected.Any(r =>
                    !conflicts.SameFolder.Contains(r.FolderName) && !conflicts.Duplicates.ContainsKey(r.FolderName));
                string prompt = LiveryRestoreConflicts.BuildDuplicatePrompt(
                    [.. duplicateRows.Select(r => (r.Data, conflicts.Duplicates[r.FolderName]))], hasOtherLiveries);
                restoreDuplicates = await confirm(prompt);
            }

            var toRestore = selected
                .Where(r => !conflicts.SameFolder.Contains(r.FolderName)
                    && (restoreDuplicates || !conflicts.Duplicates.ContainsKey(r.FolderName)))
                .ToList();

            List<string> restored = toRestore.Count > 0
                ? await _backupService.RestoreEntriesAsync(_backupPath, toRestore.Select(r => r.FolderName), savePath)
                : [];
            var restoredSet = restored.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (restoredSet.Count > 0)
            {
                foreach (var row in RemovedRows.Where(r => restoredSet.Contains(r.FolderName)).ToList())
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                    RemovedRows.Remove(row);
                }
                OnPropertyChanged(nameof(ShowNoRemovedHint));
                RaiseSelectionChanged();

                await _onRestored();
            }

            string? report = LiveryRestoreConflicts.BuildReport(
                [.. selected.Where(r => conflicts.SameFolder.Contains(r.FolderName)).Select(r => r.Data)],
                [.. toRestore.Where(r => !restoredSet.Contains(r.FolderName)).Select(r => r.Data)]);
            if (report is not null) ErrorMessageRequested?.Invoke(report);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to restore from backup", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }
}
