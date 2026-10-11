using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

internal sealed partial class MainViewModel : ObservableObject
{
    private readonly ISavePathPrompter savePathPrompter;
    private readonly SavePathService savePathService;
    private readonly CarDatabaseService carDatabase;
    private readonly CarTextProvider carTexts;
    /// <summary>The game language the shown car texts are in; when the setting or the interface language changes it, they are taken again</summary>
    private string _appliedCarTextLanguage = "";
    private readonly TagService tagService;
    private readonly AuthorCardService authorCardService;
    private readonly LiveryArchive archiveService;
    private readonly ScanController scanController;
    private readonly AppSettingsData settings;
    private readonly AccountRegistry _accountRegistry;
    private bool _changesSinceLastLaunchChecked;
    public LiveryScanEntry? LastScanResult => scanController.LastScanResult;
    public FilterBarViewModel FilterBar { get; }
    public QuickFilterPickerViewModel QuickFilterPicker { get; } = new();

    public void PrepareQuickFilterPicker() => QuickFilterPicker.Load(Gallery.AllEntries, FilterBar.ActiveManufacturerFilter);

    public void ApplyQuickFilter(QuickFilter filter) => FilterBar.ApplyQuickFilter(filter);
    public GalleryStatusViewModel Status { get; }

    public ServerStatusViewModel ServerStatus { get; } = new();

    public event Action? ScanCompleted;

    /// <summary>Once per launch, after the first scan, when liveries appeared or disappeared while the gallery was closed</summary>
    public event Action<LiveryChanges>? ChangesSinceLastLaunchFound;

    public void TurnOffChangesSinceLastLaunch()
    {
        if (!settings.ShowChangesSinceLastLaunch) return;
        settings.ShowChangesSinceLastLaunch = false;
        AppSettingsService.Save(settings);
    }
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

        RefreshGamePositions(); // the number of rows may have changed
        RefreshCarTextsIfLanguageChanged();
    }

    /// <summary>The game languages whose car texts were read from the game</summary>
    /// <summary>Empty without the game folder: car names then come from the car list, whatever is chosen</summary>
    public IReadOnlyList<ForzaToolkit.StringTables.GameLanguage> CarTextLanguages =>
        carTexts.State is GameTextsState.NoGameFolder or GameTextsState.NoStringTables ? [] : carTexts.Localization.Languages;

    /// <summary>The car texts again when their language changed (the setting, or the interface language it follows)</summary>
    public void RefreshCarTextsIfLanguageChanged()
    {
        if (!string.Equals(carTexts.LanguageCode, _appliedCarTextLanguage, StringComparison.OrdinalIgnoreCase)) RefreshCarTexts();
    }

    /// <summary>Every livery takes its car texts again; the gallery is grouped again (country and car type groups are named by them)</summary>
    public void RefreshCarTexts()
    {
        _appliedCarTextLanguage = carTexts.LanguageCode;
        foreach (var entry in Gallery.AllEntries) entry.RefreshCarTexts();
        RefreshGallery();
    }

    /// <summary>Reads the game's language archives that changed since the last time and shows the new texts</summary>
    public async Task UpdateCarTextsFromGameAsync(CancellationToken ct)
    {
        if (await carTexts.UpdateFromGameAsync(savePathService.Profile, ct)) RefreshCarTexts();
        else RefreshCarTextsIfLanguageChanged(); // a chosen language may have just appeared
    }

    public bool WasSavedPathMissing => savePathService.WasSavedPathMissing;
    public IReadOnlySet<string> SelectedTags => Gallery.SelectedTags;
    public void SetTagSelected(string tag, bool selected) => Gallery.SetTagSelected(tag, selected);

    public MainViewModel(
        AppSettingsData settings,
        ISavePathPrompter savePathPrompter,
        SavePathService savePathService,
        CarDatabaseService carDatabase,
        CarTextProvider carTexts,
        TagService tagService,
        AuthorCardService authorCardService,
        FavoriteService favoriteService,
        LiveryArchive archiveService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService,
        AccountRegistry accountRegistry)
    {
        this.settings = settings;
        _accountRegistry = accountRegistry;
        LiveryEntry.ShowFolderNamesInTooltips = settings.SearchByFolderName;
        LiveryEntry.PossibleDuplicateThresholdPercent =
            AppSettingsData.ClampPossibleDuplicateThreshold(settings.PossibleDuplicateThresholdPercent);
        this.savePathPrompter = savePathPrompter;
        this.savePathService = savePathService;
        this.carDatabase = carDatabase;
        this.carTexts = carTexts;
        LiveryEntry.CarTextSource = carTexts.For;
        this.tagService = tagService;
        this.authorCardService = authorCardService;
        this.archiveService = archiveService;

        scanController = new ScanController(scanService);
        FilterBar = new FilterBarViewModel(settings, savePathService.Profile);
        LiveryEntry.IsFindInGameAvailable = savePathService.Profile.MyDesigns is not null;
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
        InitializeNavigation();
    }

    private GalleryFilterState CurrentFilterState() => new(
        FilterBar.SearchText, FilterBar.EffectiveSortMode, FilterBar.FavoriteMode, FilterBar.MineMode, FilterBar.EffectiveInstalledMode,
        FilterBar.DuplicatesFilterMode, FilterBar.GeneratedFilterMode, FilterBar.PaintFilterMode,
        FilterBar.EffectiveAuctionFilterMode, FilterBar.GroupingEnabled, settings.SearchByFolderName, FilterBar.ActiveQuickFilters);

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
        authorCardService.BuildAliasIndex(Gallery.AllEntries.AuthorObservations());

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
        if (saveDataPath is null || savePathService.SavePath is not { } accountSavePath)
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
        // Only the first completed scan of a launch compares with what the previous launch saw
        bool collectChanges = !_changesSinceLastLaunchChecked && settings.ShowChangesSinceLastLaunch;
        string accountKey = AccountKey.For(accountSavePath);
        // The gallery may show another account (or nothing yet): then the entries are needed even if the cache did not change
        bool needEntries = !string.Equals(_appliedAccountKey, accountKey, StringComparison.OrdinalIgnoreCase);
        bool started = scanController.TryStartScan(
            accountKey, saveDataPath, savePathService.CurrentUserId, needEntries, collectChanges, progress,
            scanCts.Token, out var resultTask);

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

        // A scan of the account that was open before a switch: its cache is saved, but the gallery shows the new one
        if (result is not null && string.Equals(accountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase))
        {
            if (result.CacheChanged || needEntries)
            {
                ApplyFreshEntries(result.Entries);
                _appliedAccountKey = accountKey;
            }
            if (isUserInitiated) Status.RenderScanStatus(result);
            else if (result.Added > 0 || result.Removed > 0)
                Status.SetStatusText(string.Format(Strings.StatusAutoRefreshedFormat, result.Added, result.Removed));
            ScanCompleted?.Invoke();
            RecordCurrentAccount();

            _changesSinceLastLaunchChecked = true;
            if (result.ChangesSinceLastLaunch is { IsEmpty: false } changes) ChangesSinceLastLaunchFound?.Invoke(changes);
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
        if (savePathService.ResolveSaveDataPath() is null || CurrentAccountKey is not { } accountKey) return;

        var entries = await scanController.RegenerateEntriesAsync(accountKey, savePathService.CurrentUserId);
        if (!string.Equals(accountKey, CurrentAccountKey, StringComparison.OrdinalIgnoreCase)) return; // switched meanwhile
        ApplyFreshEntries(entries);
        _appliedAccountKey = accountKey;
    }

    private void ApplyFreshEntries(List<LiveryEntry> entries)
    {
        Gallery.ReplaceEntries(Gallery.MergeWithLocalState(entries));
        RefreshGamePositions(refreshGallery: false); // OnEntriesChanged rebuilds the gallery
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
        await OnSavePathSwitchedAsync();
    }

    public async Task HandleSavePathChangedFromSettingsAsync()
    {
        savePathService.SyncFromSettings();
        await OnSavePathSwitchedAsync();
    }

    public event Action<string>? ErrorMessageRequested;

    public Func<int, Task<(bool Confirmed, bool DontShowAgain)>>? ConfirmArchiveWithoutInstalledDataAsync { get; set; }

    public Func<int, Task<(bool Confirmed, bool DontShowAgain)>>? ConfirmArchiveWithOutdatedInstalledDataAsync { get; set; }

    public Func<Task<bool>>? IsInstalledDataOutdatedAsync { get; set; }
    /// <summary>Asked before archiving when the game is running; true to archive anyway</summary>
    public Func<Task<bool>>? ConfirmWhileGameRunningAsync { get; set; }

    private ArchiveBlock ArchiveBlockOf(LiveryEntry entry) =>
        ArchiveRules.Check(entry.IsAuction, entry.IsInstalled, Gallery.IsInstalledDataKnown);

    private bool CanArchive(LiveryEntry entry) => ArchiveBlockOf(entry) == ArchiveBlock.None;

    private bool CanMoveSelectedToArchive() => Gallery.SelectedEntries.Any(CanArchive);

    private void RefreshArchiveAvailability()
    {
        MoveSelectedToArchiveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ArchiveBlockedTooltip));
    }

    public string? ArchiveBlockedTooltip =>
        Gallery.HasSelection && !CanMoveSelectedToArchive() ? ArchiveBlockedReason(Gallery.SelectedEntries) : null;

    private string ArchiveBlockedReason(IEnumerable<LiveryEntry> blocked)
    {
        bool anyAuction = false, anyInstalled = false;
        foreach (var entry in blocked)
        {
            switch (ArchiveBlockOf(entry))
            {
                case ArchiveBlock.AuctionNeedsInstalledList: anyAuction = true; break;
                case ArchiveBlock.Installed: anyInstalled = true; break;
            }
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

    /// <summary>True when the list of applied liveries is known and comes from the save as it is now</summary>
    private async Task<bool> IsInstalledListCurrentAsync()
    {
        if (!Gallery.IsInstalledDataKnown || IsInstalledDataOutdatedAsync is not { } isOutdated) return false;
        try
        {
            return !await isOutdated();
        }
        catch (OperationCanceledException) when (ShutdownToken.IsCancellationRequested)
        {
            return false; // shutting down
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check whether the installed liveries are up to date", ex);
            return false;
        }
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
        if (!await savePathService.Profile.RunningGuard.ConfirmAsync(ConfirmWhileGameRunningAsync)) return;
        var selected = Gallery.SelectedEntries.ToList();

        // An auction livery goes only when the list of applied liveries is from the current save: a car may have
        // been given it since the last sync. Regular liveries keep the warning above, which the user can turn off
        bool auctionAllowed = !selected.Any(e => e.IsAuction && CanArchive(e)) || await IsInstalledListCurrentAsync();
        if (ShutdownToken.IsCancellationRequested) return;

        var archivable = selected.Where(e => CanArchive(e) && (!e.IsAuction || auctionAllowed)).ToList();
        var toArchive = archivable.Select(e => e.Data).ToList();
        int skippedInstalled = selected.Count(e => e.IsInstalled);
        int skippedAuction = selected.Count(e => e.IsAuction && !e.IsInstalled && !archivable.Contains(e));
        if (toArchive.Count == 0)
        {
            if (skippedAuction > 0 && skippedInstalled == 0 && Gallery.IsInstalledDataKnown)
                ErrorMessageRequested?.Invoke(string.Format(Strings.ArchiveAuctionSkippedFormat, skippedAuction));
            else if (selected.Count > 0)
                ErrorMessageRequested?.Invoke(ArchiveBlockedReason(selected));
            return;
        }

        Status.SetLoading(true, Strings.LoadingMovingToArchive);
        ArchiveOperationResult result;
        try
        {
            result = await archiveService.MoveToArchiveAsync(toArchive, savePath, GetAccountContext().Current);
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
        carTexts.Localization.LoadLocal();
        _appliedCarTextLanguage = carTexts.LanguageCode;
        savePathService.InitializeFromSettings();

        if (savePathService.SavePath is null)
        {
            await PromptForSavePathAsync(initial: true);
        }
        else
        {
            await RunScanAsyncTracked(isUserInitiated: true);
            if (shutdownToken.IsCancellationRequested) return;
            await RefreshAccountsAsync();
            await PointOutGoneAccountsAsync();
            _carDatabaseRefreshTask = RunSafelyAsync(
                () => RefreshCarDatabaseAsync(showLoadingOverlay: false, shutdownToken),
                nameof(RefreshCarDatabaseAsync));
        }

        if (shutdownToken.IsCancellationRequested) return;
        _updateCheckTask = RunSafelyAsync(() => Update.CheckAsync(shutdownToken), nameof(Update.CheckAsync));
        _carTextsUpdateTask = RunSafelyAsync(() => UpdateCarTextsFromGameAsync(shutdownToken), nameof(UpdateCarTextsFromGameAsync));
        IsInitialized = true;
    }

    private Task? _carDatabaseRefreshTask;
    private Task? _carTextsUpdateTask;

    /// <summary>After the settings: the game folder may have changed (its archives are read when they are new)</summary>
    public void StartCarTextsUpdate() =>
        _carTextsUpdateTask = RunSafelyAsync(() => UpdateCarTextsFromGameAsync(ShutdownToken), nameof(UpdateCarTextsFromGameAsync));
    private Task? _updateCheckTask;

    public async Task ShutdownAsync()
    {
        scanController.Cancel();
        await AwaitForShutdownAsync(scanController.WaitAsync(), "scan");
        await AwaitForShutdownAsync(MoveSelectedToArchiveCommand.ExecutionTask, "move to archive");
        await AwaitForShutdownAsync(NavigateInGameCommand.ExecutionTask, "in-game navigation");
        await AwaitForShutdownAsync(_accountSwitchTask, "account switch");
        await AwaitForShutdownAsync(RefreshCommand.ExecutionTask, "refresh");
        await AwaitForShutdownAsync(_carDatabaseRefreshTask, "car database refresh");
        await AwaitForShutdownAsync(_carTextsUpdateTask, "car texts update");
        await AwaitForShutdownAsync(_updateCheckTask, "update check");
        await AwaitForShutdownAsync(scanController.WaitAsync(), "entry regeneration");

        // Last, so that whatever the steps above saved is written too.
        bool allOk = await DebouncedFileWriter.Shared.FlushAsync();
        if (!allOk)
        {
            AppLogger.LogError(
                "Failed to flush one or more files on shutdown",
                new IOException("DebouncedFileWriter.Shared.FlushAsync reported at least one failed write"));
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