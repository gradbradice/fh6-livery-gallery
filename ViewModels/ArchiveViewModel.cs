using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchiveViewModel : ObservableObject
{
    private readonly LiveryArchiveService _archiveService;
    private readonly SavePathService _savePathService;
    private readonly Func<Task> _onRestored;

    public ObservableCollection<ArchivedLiveryRowViewModel> Rows { get; } = [];

    public bool ShowEmptyState => Rows.Count == 0;
    public bool HasSelection => Rows.Any(r => r.IsSelected);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, SelectedCount);
    public event Action<string>? ErrorMessageRequested;
    public event Action<List<ArchivedLiveryRowViewModel>>? DeleteRequested;

    public ArchiveViewModel(LiveryArchiveService archiveService, SavePathService savePathService, Func<Task> onRestored)
    {
        _archiveService = archiveService;
        _savePathService = savePathService;
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

        var folderNames = selected.Select(r => r.FolderName).ToList();
        try
        {
            var restored = await _archiveService.RestoreAsync(folderNames, savePath);
            if (restored.Count == 0) return;

            Reload();
            await _onRestored();
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
