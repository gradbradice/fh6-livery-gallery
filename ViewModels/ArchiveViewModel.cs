using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchiveViewModel : ObservableObject
{
    private readonly LiveryArchiveService _archiveService;
    private readonly SavePathService _savePathService;
    private readonly Func<Task> _onRestored;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    public ObservableCollection<ArchivedLiveryRowViewModel> Rows { get; } = [];
    public bool ShowEmptyState => Rows.Count == 0;
    public bool HasSelection => Rows.Any(r => r.IsSelected);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, SelectedCount);
    public event Action<string>? ErrorMessageRequested;
    public Func<string, Task<bool>>? ConfirmRestoreDuplicatesAsync { get; set; }
    public event Action<List<ArchivedLiveryRowViewModel>>? DeleteRequested;

    public ArchiveViewModel(
        LiveryArchiveService archiveService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored)
    {
        _archiveService = archiveService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        _onRestored = onRestored;
        Reload();
    }

    private void Reload()
    {
        foreach (var row in Rows) row.PropertyChanged -= OnRowPropertyChanged;
        Rows.Clear();

        foreach (var entry in _archiveService.GetAll())
        {
            var row = new ArchivedLiveryRowViewModel(entry);
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(ShowEmptyState));
        RaiseSelectionChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArchivedLiveryRowViewModel.IsSelected)) RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows) row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
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
                selected.Select(r => r.Entry.Data), _getCurrentEntries(), savePath);

            var duplicateRows = selected.Where(r => conflicts.Duplicates.ContainsKey(r.FolderName)).ToList();
            bool restoreDuplicates = false;
            if (duplicateRows.Count > 0 && ConfirmRestoreDuplicatesAsync is { } confirm)
            {
                bool hasOtherLiveries = selected.Any(r =>
                    !conflicts.SameFolder.Contains(r.FolderName) && !conflicts.Duplicates.ContainsKey(r.FolderName));
                string prompt = LiveryRestoreConflicts.BuildDuplicatePrompt(
                    [.. duplicateRows.Select(r => (r.Entry.Data, conflicts.Duplicates[r.FolderName]))], hasOtherLiveries);
                restoreDuplicates = await confirm(prompt);
            }

            var toRestore = selected
                .Where(r => !conflicts.SameFolder.Contains(r.FolderName)
                    && (restoreDuplicates || !conflicts.Duplicates.ContainsKey(r.FolderName)))
                .ToList();

            List<string> restored = toRestore.Count > 0
                ? await _archiveService.RestoreAsync(toRestore.Select(r => r.FolderName), savePath)
                : [];
            var restoredSet = restored.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (restoredSet.Count > 0)
            {
                Reload();
                await _onRestored();
            }

            string? report = LiveryRestoreConflicts.BuildReport(
                [.. selected.Where(r => conflicts.SameFolder.Contains(r.FolderName)).Select(r => r.Entry.Data)],
                [.. toRestore.Where(r => !restoredSet.Contains(r.FolderName)).Select(r => r.Entry.Data)]);
            if (report is not null) ErrorMessageRequested?.Invoke(report);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to restore from archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }

    [RelayCommand]
    private void RequestDelete()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;
        DeleteRequested?.Invoke(selected);
    }

    public async Task DeleteConfirmedAsync(List<ArchivedLiveryRowViewModel> rows)
    {
        var folderNames = rows.Select(r => r.FolderName).ToList();
        try
        {
            await _archiveService.DeletePermanentlyAsync(folderNames);
            Reload();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to delete from archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }
}
