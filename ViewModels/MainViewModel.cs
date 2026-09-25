using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Controller;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class MainViewModel : ObservableObject
{
    private readonly ISavePathPrompter savePathPrompter;
    private readonly SavePathService savePathService;
    private readonly CarDatabaseService carDatabase;
    private readonly TagService tagService;
    private readonly AuthorCardService authorCardService;
    private readonly LiveryArchiveService archiveService;
    private readonly ScanController scanController;
    private readonly AppSettingsData settings;
    private bool _hasAppliedEntriesOnce;
    public LiveryScanEntry? LastScanResult => scanController.LastScanResult;
    public FilterBarViewModel FilterBar { get; }
    public GalleryStatusViewModel Status { get; }
    public GalleryViewModel Gallery { get; }
    public TagsBarViewModel TagsBar { get; }
    public UpdateViewModel Update { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public CancellationToken ShutdownToken { get; set; }
    [ObservableProperty]
    private bool _isInitialized;
    public bool ShowFolderNames => settings.SearchByFolderName;
    public void RefreshDisplaySettings()
    {
        OnPropertyChanged(nameof(ShowFolderNames));
        if (LiveryEntry.ShowFolderNamesInTooltips != settings.SearchByFolderName)
        {
            LiveryEntry.ShowFolderNamesInTooltips = settings.SearchByFolderName;
            Gallery.RefreshDuplicateTooltips();
        }
    }

    public bool WasSavedPathMissing => savePathService.WasSavedPathMissing;
    public IReadOnlySet<string> SelectedTags => Gallery.SelectedTags;
    public void SetTagSelected(string tag, bool selected) => Gallery.SetTagSelected(tag, selected);

    public MainViewModel(
        AppSettingsData settings,
        ISavePathPrompter savePathPrompter,
        SavePathService savePathService,
        CarDatabaseService carDatabase,
        TagService tagService,
        AuthorCardService authorCardService,
        FavoriteService favoriteService,
        LiveryArchiveService archiveService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService)
    {
        this.settings = settings;
        LiveryEntry.ShowFolderNamesInTooltips = settings.SearchByFolderName;
        this.savePathPrompter = savePathPrompter;
        this.savePathService = savePathService;
        this.carDatabase = carDatabase;
        this.tagService = tagService;
        this.authorCardService = authorCardService;
        this.archiveService = archiveService;

        scanController = new ScanController(scanService);
        FilterBar = new FilterBarViewModel(settings);
        Status = new GalleryStatusViewModel();
        Gallery = new GalleryViewModel(favoriteService);
        TagsBar = new TagsBarViewModel(SelectedTags, SetTagSelected);
        Update = new UpdateViewModel(updateService);
        Gallery.CountsUpdated += snapshot =>
            Status.UpdateCountsAndEmptyState(snapshot.FilteredEntries, snapshot.TotalCount, snapshot.SearchText);
        FilterBar.FiltersChanged += RefreshGallery;
        RefreshCommand = new AsyncRelayCommand(RefreshButtonClickedAsync);
    }

    private GalleryFilterState CurrentFilterState() => new(
        FilterBar.SearchText, FilterBar.SortMode, FilterBar.FavoriteMode, FilterBar.MineMode,
        FilterBar.DuplicatesFilterMode, FilterBar.GeneratedFilterMode, FilterBar.PaintFilterMode,
        FilterBar.GroupingEnabled, settings.SearchByFolderName);

    public void RefreshGallery() => Gallery.Refresh(CurrentFilterState());

    public void RebuildTagsBar()
    {
        TagsBar.RecomputeAllTags(Gallery.AllEntries);
        Gallery.SyncKnownTags([.. TagsBar.AllTags.Select(t => t.Tag)]);
    }

    public void ApplyTagsEdit(LiveryEntry entry, List<string> newTags)
    {
        entry.Tags = newTags;
        tagService.SetTags(entry.FolderName, newTags);
        RebuildTagsBar();

        if (Gallery.SelectedTags.Count > 0) RefreshGallery();
    }

    public AuthorCard? FindAuthorCard(LiveryEntry entry) =>
        entry.AuthorIdentityTagHex is not null ? authorCardService.FindCardForIdentityTag(entry.AuthorIdentityTagHex) : null;

    public (List<string> AvailableAliases, Dictionary<string, List<string>> NameToTags) BuildAuthorAliasIndex() =>
        authorCardService.BuildAliasIndex(Gallery.AllEntries);

    public async Task<bool> TrySaveAuthorCard(AuthorCard card)
    {
        if (!await authorCardService.TrySave(card)) return false;
        await RefreshEntriesFromCacheAsync();
        return true;
    }

    public string GetOverallStatsMessage()
    {
        var stats = GalleryStatisticsService.CalculateOverall([.. Gallery.AllEntries]);
        return GalleryStatisticsService.FormatOverallMessage(stats);
    }

    private void OnEntriesChanged()
    {
        RebuildTagsBar();
        RefreshGallery();
    }

    public async Task RunScanAsyncTracked(bool isUserInitiated)
    {
        string? saveDataPath = savePathService.ResolveSaveDataPath();
        if (saveDataPath is null)
        {
            if (savePathService.ShouldNotifyLost())
            {
                await PromptForSavePathAsync(initial: true);
            }
            return;
        }
        savePathService.ResetLostNotification();

        if (!isUserInitiated && !settings.AutoRefreshLiveries) return;

        var progress = isUserInitiated ? new Progress<string>(msg => Status.SetLoading(true, msg)) : null;
        bool started = scanController.TryStartScan(
            saveDataPath, savePathService.CurrentUserId, progress, out var resultTask);

        if (!started)
        {
            await scanController.WaitAsync();
            return;
        }

        if (isUserInitiated)
        {
            Status.SetLoading(true, Strings.LoadingScanning);
            Status.IsScanRunning = true;
        }

        LiveryScanEntry? result = null;
        Exception? scanError = null;
        try
        {
            result = await resultTask;
        }
        catch (Exception ex)
        {
            scanError = ex;
        }

        if (isUserInitiated)
        {
            Status.SetLoading(false);
            Status.IsScanRunning = false;
        }

        if (result is not null)
        {
            if (result.CacheChanged || !_hasAppliedEntriesOnce)
            {
                ApplyFreshEntries(result.Entries);
                _hasAppliedEntriesOnce = true;
            }
            if (isUserInitiated) Status.RenderScanStatus(result);
        }

        if (scanError is not null)
        {
            AppLogger.LogErrorThrottled(saveDataPath, $"Scan failed: '{saveDataPath}'", scanError);
            if (isUserInitiated)
                Status.SetStatusText(string.Format(Strings.StatusScanError, scanError.Message));
        }
    }

    public async Task RefreshEntriesFromCacheAsync()
    {
        if (savePathService.ResolveSaveDataPath() is null) return;

        var entries = await scanController.RegenerateEntriesAsync(savePathService.CurrentUserId);
        ApplyFreshEntries(entries);
    }

    private void ApplyFreshEntries(List<LiveryEntry> entries)
    {
        Gallery.ReplaceEntries(Gallery.MergeWithLocalState(entries));
        OnEntriesChanged();
    }

    public async Task RefreshCarDatabaseAsync(bool showLoadingOverlay, CancellationToken shutdownToken)
    {
        if (showLoadingOverlay) Status.SetLoading(true, Strings.CarDbUpdating);
        try
        {
            var outcome = await carDatabase.RefreshAsync(shutdownToken);
            string countText = $"{carDatabase.Count} {Strings.CarDbCarsWord}";
            if (carDatabase.LastError is not null)
            {
                Status.SetStatusText(carDatabase.HasLocalData
                    ? string.Format(Strings.CarDbDownloadFailed, countText)
                    : Strings.CarDbNoDataAtAll);
            }
            else
            {
                Status.SetStatusText(outcome == CarDatabaseRefreshOutcome.Updated
                    ? string.Format(Strings.CarDbUpdated, countText)
                    : string.Format(Strings.CarDbUpToDate, countText));
            }

            if (outcome == CarDatabaseRefreshOutcome.Updated)
                await RefreshEntriesFromCacheAsync();
        }
        catch (OperationCanceledException)
        {

        }
        finally
        {
            if (showLoadingOverlay) Status.SetLoading(false);
        }
    }

    public async Task PromptForSavePathAsync(bool initial)
    {
        string? path = await savePathPrompter.PromptForSavePathAsync(initial);
        if (path is null)
        {
            Status.SetStatusText(Strings.SavePathNotChosen);
            Status.ShowEmptyState(Strings.SavePathNotChosen);
            return;
        }

        savePathService.SetSavePath(path);
        await RunScanAsyncTracked(isUserInitiated: true);
    }

    public async Task HandleSavePathChangedFromSettingsAsync()
    {
        savePathService.SyncFromSettings();
        if (savePathService.ResolveSaveDataPath() is not null)
            await RunScanAsyncTracked(isUserInitiated: true);
    }

    public event Action<string>? ErrorMessageRequested;

    [RelayCommand]
    private async Task MoveSelectedToArchiveAsync()
    {
        if (!Gallery.HasSelection) return;
        string? savePath = savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return;
        }

        var toArchive = Gallery.SelectedEntries.Select(e => e.Data).ToList();
        Status.SetLoading(true, Strings.LoadingMovingToArchive);
        try
        {
            await archiveService.MoveToArchiveAsync(toArchive, savePath);
            Gallery.ClearSelectionCommand.Execute(null);
            await RunScanAsyncTracked(isUserInitiated: true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to move selected liveries to archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
        }
        finally
        {
            Status.SetLoading(false);
        }
    }

    private async Task RefreshButtonClickedAsync()
    {
        await Task.WhenAll(
            RefreshCarDatabaseAsync(showLoadingOverlay: false, ShutdownToken),
            Update.CheckAsync(ShutdownToken));

        if (!settings.RefreshLiveriesOnButtonClick) return;

        if (savePathService.SavePath is null)
        {
            await PromptForSavePathAsync(initial: true);
            return;
        }
        await RunScanAsyncTracked(isUserInitiated: true);
    }

    public async Task InitializeAsync(CancellationToken shutdownToken)
    {
        carDatabase.LoadLocal();
        savePathService.InitializeFromSettings();

        if (savePathService.SavePath is null)
        {
            await PromptForSavePathAsync(initial: true);
        }
        else
        {
            await RunScanAsyncTracked(isUserInitiated: true);
            _carDatabaseRefreshTask = RunSafelyAsync(
                () => RefreshCarDatabaseAsync(showLoadingOverlay: false, shutdownToken),
                nameof(RefreshCarDatabaseAsync));
        }

        _updateCheckTask = RunSafelyAsync(() => Update.CheckAsync(shutdownToken), nameof(Update.CheckAsync));
        IsInitialized = true;
    }

    private Task? _carDatabaseRefreshTask;
    private Task? _updateCheckTask;

    public async Task ShutdownAsync()
    {
        scanController.Cancel();

        try
        {
            await scanController.WaitAsync();
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unexpected error while waiting for scan to stop during shutdown", ex);
        }

        bool allOk = await PersistenceManager.FlushAsync();
        if (!allOk)
        {
            AppLogger.LogError(
                "Failed to flush one or more files on shutdown",
                new IOException("PersistenceManager.FlushAsync reported at least one failed write"));
        }

        if (_carDatabaseRefreshTask is not null) await _carDatabaseRefreshTask;
        if (_updateCheckTask is not null) await _updateCheckTask;

        AppLogger.Shutdown();
    }

    private static async Task RunSafelyAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Unhandled exception in background task: {context}", ex);
        }
    }
}