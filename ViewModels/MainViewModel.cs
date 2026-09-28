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
    public QuickFilterPickerViewModel QuickFilterPicker { get; } = new();

    public void PrepareQuickFilterPicker() => QuickFilterPicker.Load(Gallery.AllEntries, FilterBar.ActiveManufacturerFilter);

    public void ApplyQuickFilter(QuickFilter filter) => FilterBar.ApplyQuickFilter(filter);
    public GalleryStatusViewModel Status { get; }

    public ServerStatusViewModel ServerStatus { get; } = new();

    public event Action? ScanCompleted;
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

        int threshold = AppSettingsData.ClampPossibleDuplicateThreshold(settings.PossibleDuplicateThresholdPercent);
        if (LiveryEntry.PossibleDuplicateThresholdPercent != threshold)
        {
            LiveryEntry.PossibleDuplicateThresholdPercent = threshold;
            Gallery.RefreshPossibleDuplicateThreshold();
            RefreshGallery();
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
        LiveryEntry.PossibleDuplicateThresholdPercent =
            AppSettingsData.ClampPossibleDuplicateThreshold(settings.PossibleDuplicateThresholdPercent);
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
        Gallery.SelectedTagsChanged += TagsBar.SyncSelection;
        Update = new UpdateViewModel(updateService);
        Gallery.CountsUpdated += snapshot =>
            Status.UpdateCountsAndEmptyState(snapshot.FilteredEntries, snapshot.TotalCount, snapshot.SearchText);
        Gallery.SelectedEntries.CollectionChanged += (_, __) => RefreshArchiveAvailability();
        Gallery.InstalledCountsChanged += RefreshArchiveAvailability;
        FilterBar.FiltersChanged += RefreshGallery;
        RefreshCommand = new AsyncRelayCommand(RefreshButtonClickedAsync);
    }

    private GalleryFilterState CurrentFilterState() => new(
        FilterBar.SearchText, FilterBar.SortMode, FilterBar.FavoriteMode, FilterBar.MineMode, FilterBar.EffectiveInstalledMode,
        FilterBar.DuplicatesFilterMode, FilterBar.GeneratedFilterMode, FilterBar.PaintFilterMode,
        FilterBar.AuctionFilterMode, FilterBar.GroupingEnabled, settings.SearchByFolderName, FilterBar.ActiveQuickFilters);

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

    public async Task RunScanAsyncTracked(bool isUserInitiated) => await RunScanCoreAsync(isUserInitiated);

    public Task<bool> RunAutoRefreshAsync(CancellationToken ct = default) => RunScanCoreAsync(isUserInitiated: false, ct);

    public event Action? SavePathChanged;

    private async Task<bool> RunScanCoreAsync(bool isUserInitiated, CancellationToken ct = default)
    {
        if (ShutdownToken.IsCancellationRequested || ct.IsCancellationRequested) return true;
        string? saveDataPath = savePathService.ResolveSaveDataPath();
        if (saveDataPath is null)
        {
            if (savePathService.ShouldNotifyLost())
            {
                await PromptForSavePathAsync(initial: true);
            }
            return true;
        }
        savePathService.ResetLostNotification();

        if (!isUserInitiated && !settings.AutoRefreshLiveries) return true;

        var progress = isUserInitiated ? new Progress<string>(msg => Status.SetLoading(true, msg)) : null;
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken, ct);
        bool started = scanController.TryStartScan(
            saveDataPath, savePathService.CurrentUserId, needEntries: !_hasAppliedEntriesOnce, progress, scanCts.Token,
            out var resultTask);

        if (!started)
        {
            await scanController.WaitAsync();
            return false;
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
        catch (OperationCanceledException) when (scanCts.IsCancellationRequested)
        {
            // Stopped on purpose. Not an error, nothing to apply
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
            else if (result.Added > 0 || result.Removed > 0)
                Status.SetStatusText(string.Format(Strings.StatusAutoRefreshedFormat, result.Added, result.Removed));
            ScanCompleted?.Invoke();
        }

        if (scanError is not null)
        {
            AppLogger.LogErrorThrottled(saveDataPath, $"Scan failed: '{saveDataPath}'", scanError);
            if (isUserInitiated)
                Status.SetStatusText(string.Format(Strings.StatusScanError, scanError.Message));
        }
        return true;
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
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            // Shutting down
        }
        finally
        {
            if (showLoadingOverlay) Status.SetLoading(false);
        }
    }

    public async Task PromptForSavePathAsync(bool initial)
    {
        string? path = await savePathPrompter.PromptForSavePathAsync(initial, ShutdownToken);
        if (ShutdownToken.IsCancellationRequested) return;
        if (path is null)
        {
            Status.SetStatusText(Strings.SavePathNotChosen);
            Status.ShowEmptyState(Strings.SavePathNotChosen);
            return;
        }

        savePathService.SetSavePath(path);
        SavePathChanged?.Invoke();
        await RunScanAsyncTracked(isUserInitiated: true);
    }

    public async Task HandleSavePathChangedFromSettingsAsync()
    {
        savePathService.SyncFromSettings();
        SavePathChanged?.Invoke();
        if (savePathService.ResolveSaveDataPath() is not null)
            await RunScanAsyncTracked(isUserInitiated: true);
    }

    public event Action<string>? ErrorMessageRequested;

    public Func<int, Task<(bool Confirmed, bool DontShowAgain)>>? ConfirmArchiveWithoutInstalledDataAsync { get; set; }

    public Func<int, Task<(bool Confirmed, bool DontShowAgain)>>? ConfirmArchiveWithOutdatedInstalledDataAsync { get; set; }

    public Func<Task<bool>>? IsInstalledDataOutdatedAsync { get; set; }

    private static bool CanArchive(LiveryEntry entry) => !entry.IsAuction && !entry.IsInstalled;

    private bool CanMoveSelectedToArchive() => Gallery.SelectedEntries.Any(CanArchive);

    private void RefreshArchiveAvailability()
    {
        MoveSelectedToArchiveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ArchiveBlockedTooltip));
    }

    public string? ArchiveBlockedTooltip =>
        Gallery.HasSelection && !CanMoveSelectedToArchive() ? ArchiveBlockedReason(Gallery.SelectedEntries) : null;

    private static string ArchiveBlockedReason(IEnumerable<LiveryEntry> blocked)
    {
        bool anyAuction = false, anyInstalled = false;
        foreach (var entry in blocked)
        {
            if (entry.IsAuction) anyAuction = true;
            else if (entry.IsInstalled) anyInstalled = true;
        }
        return (anyAuction, anyInstalled) switch
        {
            (true, true) => Strings.ArchiveAuctionOrInstalledNotAllowedTooltip,
            (false, true) => Strings.ArchiveInstalledNotAllowedTooltip,
            _ => Strings.ArchiveAuctionNotAllowedTooltip,
        };
    }

    private async Task<bool> ConfirmArchiveRiskAsync()
    {
        int CountToArchive() => Gallery.SelectedEntries.Count(CanArchive);

        if (!Gallery.IsInstalledDataKnown)
        {
            if (!settings.WarnArchiveWithoutInstalledData || ConfirmArchiveWithoutInstalledDataAsync is not { } askUnknown)
                return true;
            return await AskAndRememberAsync(askUnknown(CountToArchive()), s => s.WarnArchiveWithoutInstalledData = false);
        }

        if (!settings.WarnArchiveWithOutdatedInstalledData
            || IsInstalledDataOutdatedAsync is not { } isOutdated
            || ConfirmArchiveWithOutdatedInstalledDataAsync is not { } askOutdated)
            return true;

        bool outdated;
        try
        {
            outdated = await isOutdated();
        }
        catch (OperationCanceledException) when (ShutdownToken.IsCancellationRequested)
        {
            return false; // shutting down
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check whether the installed liveries are up to date", ex);
            outdated = true;
        }
        if (ShutdownToken.IsCancellationRequested) return false;
        if (!outdated) return true;

        // Counted after the check. New data may have arrived meanwhile and blocked some of the selection
        int count = CountToArchive();
        if (count == 0) return true; // the caller explains why nothing can be archived
        return await AskAndRememberAsync(askOutdated(count), s => s.WarnArchiveWithOutdatedInstalledData = false);
    }

    private async Task<bool> AskAndRememberAsync(
        Task<(bool Confirmed, bool DontShowAgain)> question, Action<AppSettingsData> turnWarningOff)
    {
        var (confirmed, dontShowAgain) = await question;
        if (confirmed && dontShowAgain)
        {
            turnWarningOff(settings);
            AppSettingsService.Save(settings);
        }
        return confirmed;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedToArchive))]
    private async Task MoveSelectedToArchiveAsync()
    {
        if (!Gallery.HasSelection) return;
        string? savePath = savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return;
        }

        if (!Gallery.SelectedEntries.Any(CanArchive))
        {
            ErrorMessageRequested?.Invoke(ArchiveBlockedReason(Gallery.SelectedEntries));
            return;
        }

        if (!await ConfirmArchiveRiskAsync()) return;
        var selected = Gallery.SelectedEntries.ToList();
        var toArchive = selected.Where(CanArchive).Select(e => e.Data).ToList();
        int skippedAuction = selected.Count(e => e.IsAuction);
        int skippedInstalled = selected.Count(e => !e.IsAuction && e.IsInstalled);
        if (toArchive.Count == 0)
        {
            if (selected.Count > 0) ErrorMessageRequested?.Invoke(ArchiveBlockedReason(selected));
            return;
        }

        Status.SetLoading(true, Strings.LoadingMovingToArchive);
        ArchiveOperationResult result;
        try
        {
            result = await archiveService.MoveToArchiveAsync(toArchive, savePath);
            Gallery.ClearSelectionCommand.Execute(null);
            await RunScanAsyncTracked(isUserInitiated: true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to move selected liveries to archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return;
        }
        finally
        {
            Status.SetLoading(false);
        }

        var notes = new List<string>(3);
        if (skippedAuction > 0) notes.Add(string.Format(Strings.ArchiveAuctionSkippedFormat, skippedAuction));
        if (skippedInstalled > 0) notes.Add(string.Format(Strings.ArchiveInstalledSkippedFormat, skippedInstalled));
        if (result.LeftoverPaths.Count > 0)
            notes.Add(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join(Environment.NewLine, result.LeftoverPaths)));
        if (notes.Count > 0) ErrorMessageRequested?.Invoke(string.Join(Environment.NewLine + Environment.NewLine, notes));
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
            if (shutdownToken.IsCancellationRequested) return;
            _carDatabaseRefreshTask = RunSafelyAsync(
                () => RefreshCarDatabaseAsync(showLoadingOverlay: false, shutdownToken),
                nameof(RefreshCarDatabaseAsync));
        }

        if (shutdownToken.IsCancellationRequested) return;
        _updateCheckTask = RunSafelyAsync(() => Update.CheckAsync(shutdownToken), nameof(Update.CheckAsync));
        IsInitialized = true;
    }

    private Task? _carDatabaseRefreshTask;
    private Task? _updateCheckTask;

    public async Task ShutdownAsync()
    {
        scanController.Cancel();
        await AwaitForShutdownAsync(scanController.WaitAsync(), "scan");
        await AwaitForShutdownAsync(MoveSelectedToArchiveCommand.ExecutionTask, "move to archive");
        await AwaitForShutdownAsync(RefreshCommand.ExecutionTask, "refresh");
        await AwaitForShutdownAsync(_carDatabaseRefreshTask, "car database refresh");
        await AwaitForShutdownAsync(_updateCheckTask, "update check");
        await AwaitForShutdownAsync(scanController.WaitAsync(), "entry regeneration");

        // Last, so that whatever the steps above saved is written too.
        bool allOk = await PersistenceManager.FlushAsync();
        if (!allOk)
        {
            AppLogger.LogError(
                "Failed to flush one or more files on shutdown",
                new IOException("PersistenceManager.FlushAsync reported at least one failed write"));
        }
    }

    private static async Task AwaitForShutdownAsync(Task? task, string what)
    {
        if (task is null) return;
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the shutdown itself
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Background work '{what}' failed while shutting down", ex);
        }
    }

    private static async Task RunSafelyAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Background tasks are only cancelled by the shutdown
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Unhandled exception in background task: {context}", ex);
        }
    }
}