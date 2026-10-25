using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchiveViewModel : ObservableObject
{
    private readonly LiveryArchive _archiveService;
    private readonly SavePathService _savePathService;
    private readonly Func<Task> _onRestored;
    private readonly Func<IReadOnlyList<LiveryData>> _getCurrentEntries;
    private readonly AccountContext _accounts;

    // Every row of the archive (after the account filter); Rows are the ones the search leaves
    private readonly List<ArchivedLiveryRowViewModel> _allRows = [];

    public ObservableCollection<ArchivedLiveryRowViewModel> Rows { get; } = [];
    public bool ShowEmptyState => Rows.Count == 0;

    /// <summary>The archive (after the account filter) has liveries: the search box is shown</summary>
    public bool HasAnyRows => _allRows.Count > 0;

    /// <summary>"Nothing found" when the search hides every livery, else "the archive is empty"</summary>
    public string EmptyStateText => HasAnyRows ? Strings.ArchiveNoSearchResultsHint : Strings.ArchiveEmptyHint;

    /// <summary>The setting "show folder names and search by them"</summary>
    public bool SearchByFolderName { get; }

    public string SearchPlaceholder => SearchByFolderName ? Strings.ArchiveSearchPlaceholderWithFolder : Strings.ArchiveSearchPlaceholder;

    /// <summary>Words to look for, all of them, in the car, year, name, author, number (and folder name)</summary>
    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => ApplySearch();

    /// <summary>On by default: another account's liveries are only shown when asked for</summary>
    [ObservableProperty]
    private bool _onlyCurrentAccount = true;

    /// <summary>The filter is only offered when the archive has liveries of another account at all</summary>
    public bool CanFilterByAccount { get; private set; }
    public int HiddenByAccountCount { get; private set; }
    public bool HasHiddenByAccount => HiddenByAccountCount > 0;
    public string HiddenByAccountText => string.Format(Strings.AccountFilterHiddenFormat, HiddenByAccountCount);

    partial void OnOnlyCurrentAccountChanged(bool value) => Reload();
    public bool HasSelection => Rows.Any(r => r.IsSelected);
    public int SelectedCount => Rows.Count(r => r.IsSelected);
    public string SelectedCountText => string.Format(Strings.ArchiveSelectedCountFormat, SelectedCount);
    public event Action<string>? ErrorMessageRequested;
    public Func<string, Task<bool>>? ConfirmRestoreDuplicatesAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmRestoreIntoCurrentAccountAsync { get; set; }
    /// <summary>Asked when the game is running; true to restore anyway</summary>
    public Func<Task<bool>>? ConfirmWhileGameRunningAsync { get; set; }
    public event Action<List<ArchivedLiveryRowViewModel>>? DeleteRequested;

    public ArchiveViewModel(
        LiveryArchive archiveService, SavePathService savePathService,
        Func<IReadOnlyList<LiveryData>> getCurrentEntries, Func<Task> onRestored, AccountContext accounts,
        bool searchByFolderName = false)
    {
        SearchByFolderName = searchByFolderName;
        _accounts = accounts;
        _archiveService = archiveService;
        _savePathService = savePathService;
        _getCurrentEntries = getCurrentEntries;
        _onRestored = onRestored;
        Reload();
    }

    private void Reload()
    {
        foreach (var row in _allRows) row.PropertyChanged -= OnRowPropertyChanged;
        _allRows.Clear();

        // Liveries of unknown origin (archived before accounts were recorded) and of accounts that are gone are always shown
        var all = _archiveService.GetAll();
        int fromOtherAccounts = all.Count(e => _accounts.IsHiddenByAccountFilter(e.SourceAccount));
        CanFilterByAccount = fromOtherAccounts > 0;
        HiddenByAccountCount = OnlyCurrentAccount ? fromOtherAccounts : 0;
        OnPropertyChanged(nameof(CanFilterByAccount));
        OnPropertyChanged(nameof(HiddenByAccountCount));
        OnPropertyChanged(nameof(HasHiddenByAccount));
        OnPropertyChanged(nameof(HiddenByAccountText));

        foreach (var entry in all)
        {
            if (OnlyCurrentAccount && _accounts.IsHiddenByAccountFilter(entry.SourceAccount)) continue;
            var row = new ArchivedLiveryRowViewModel(entry, _accounts, SearchByFolderName);
            row.PropertyChanged += OnRowPropertyChanged;
            _allRows.Add(row);
        }

        ApplySearch();
    }

    // Shows the rows the search leaves. A row the search hides is unselected: restore and delete act on what is seen
    private void ApplySearch()
    {
        string[] tokens = SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Rows.Clear();
        foreach (var row in _allRows)
        {
            if (tokens.Length == 0 || row.MatchesSearch(tokens)) Rows.Add(row);
            else row.IsSelected = false;
        }

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasAnyRows));
        OnPropertyChanged(nameof(EmptyStateText));
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

        // Restoring another account's liveries here is allowed (it moves them between accounts), but not by accident
        if (_accounts.BuildRestoreIntoCurrentPrompt(selected.Select(r => r.Entry.SourceAccount)) is { } accountPrompt
            && ConfirmRestoreIntoCurrentAccountAsync is { } confirmAccount
            && !await confirmAccount(accountPrompt))
            return;

        if (!await _savePathService.Profile.RunningGuard.ConfirmAsync(ConfirmWhileGameRunningAsync)) return;

        try
        {
            var plan = await LiveryRestoreConflicts.PlanAsync(
                [.. selected.Select(r => r.Entry.Data)], _getCurrentEntries(), savePath, _savePathService.Profile.Liveries,
                ConfirmRestoreDuplicatesAsync);

            var result = plan.ToRestore.Count > 0
                ? await _archiveService.RestoreAsync(plan.ToRestore.Select(d => d.FolderName), savePath)
                : ArchiveOperationResult.Empty;
            var restoredSet = result.Completed.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (restoredSet.Count > 0)
            {
                Reload();
                await _onRestored();
            }

            string? report = plan.Report(restoredSet);
            if (report is not null) ErrorMessageRequested?.Invoke(report);
            if (result.LeftoverPaths.Count > 0)
                ErrorMessageRequested?.Invoke(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join("\n", result.LeftoverPaths)));
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to restore from archive", ex);
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
            AppLog.LogError("Failed to delete from archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
    }
}
