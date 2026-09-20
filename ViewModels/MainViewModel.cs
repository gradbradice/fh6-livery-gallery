using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Controller;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed class MainViewModel
{
    private readonly SavePathService savePathService;
    private readonly CarDatabaseService carDatabase;
    private readonly AppSettingsData settings;
    private bool _hasAppliedEntriesOnce;

    public ScanController ScanController { get; }
    public FilterBarViewModel FilterBar { get; }
    public GalleryStatusViewModel Status { get; }
    public GalleryViewModel Gallery { get; }
    public TagsBarViewModel TagsBar { get; }
    public UpdateViewModel Update { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public CancellationToken ShutdownToken { get; set; }
    public event Action<LiveryEntry>? AuthorRowRequested;
    public event Action<LiveryEntry>? EditTagsRequested;

    public MainViewModel(
        AppSettingsData settings,
        SavePathService savePathService,
        CarDatabaseService carDatabase,
        FavoriteService favoriteService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService)
    {
        this.settings = settings;
        this.savePathService = savePathService;
        this.carDatabase = carDatabase;

        ScanController = new ScanController(scanService);
        FilterBar = new FilterBarViewModel(settings);
        Status = new GalleryStatusViewModel();
        Gallery = new GalleryViewModel(FilterBar, Status, favoriteService);
        TagsBar = new TagsBarViewModel(Gallery.SelectedTags, Gallery.SetTagSelected);
        Update = new UpdateViewModel(updateService);

        Gallery.AuthorRowRequested += entry => AuthorRowRequested?.Invoke(entry);
        Gallery.EditTagsRequested += entry => EditTagsRequested?.Invoke(entry);
        FilterBar.FiltersChanged += RefreshGallery;
        RefreshCommand = new AsyncRelayCommand(RefreshButtonClickedAsync);
    }

    public void RefreshGallery() => Gallery.Refresh();

    public void RebuildTagsBar()
    {
        TagsBar.RecomputeAllTags(Gallery.AllEntries);
        Gallery.SyncKnownTags([.. TagsBar.AllTags.Select(t => t.Tag)]);
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
        bool started = ScanController.TryStartScan(
            saveDataPath, savePathService.CurrentUserId, progress, out var resultTask);

        if (!started)
        {
            await ScanController.WaitAsync();
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

        await ScanController.RegenerateEntriesAsync(savePathService.CurrentUserId, ApplyFreshEntries);
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
        bool selected = await savePathService.PromptAsync(initial, onDeclined: () =>
        {
            Status.SetStatusText(Strings.SavePathNotChosen);
            Status.ShowEmptyState(Strings.SavePathNotChosen);
        });

        if (selected) await RunScanAsyncTracked(isUserInitiated: true);
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
}