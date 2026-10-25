using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

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

        int threshold = PossibleDuplicateThreshold.Clamp(settings.PossibleDuplicateThresholdPercent);
        if (LiveryEntry.PossibleDuplicateThresholdPercent != threshold)
        {
            LiveryEntry.PossibleDuplicateThresholdPercent = threshold;
            Gallery.RefreshPossibleDuplicateThreshold();
            RefreshGallery();
        }

        RefreshGamePositions(); // the number of rows may have changed
        RefreshCarTextsIfLanguageChanged();
    }

    /// <summary>
    /// The game languages there are car texts in: read from the game, else published with the car list; empty when
    /// neither has any (car names then come from the car list, whatever is chosen)
    /// </summary>
    public IReadOnlyList<ForzaToolkit.StringTables.GameLanguage> CarTextLanguages => carTexts.Languages;

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

    private readonly ForzaToolkit.Liveries.History.LiveryHistoryStore? _history;
    private readonly LiveryScanner _scanService;

    public bool WasSavedPathMissing => savePathService.WasSavedPathMissing;
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
        LockService lockService,
        LiveryArchive archiveService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService,
        AccountRegistry accountRegistry)
    {
        this.settings = settings;
        _accountRegistry = accountRegistry;
        LiveryEntry.ShowFolderNamesInTooltips = settings.SearchByFolderName;
        LiveryEntry.PossibleDuplicateThresholdPercent =
            PossibleDuplicateThreshold.Clamp(settings.PossibleDuplicateThresholdPercent);
        this.savePathPrompter = savePathPrompter;
        this.savePathService = savePathService;
        this.carDatabase = carDatabase;
        this.carTexts = carTexts;
        LiveryEntry.CarTextSource = carTexts.For;
        this.tagService = tagService;
        this.authorCardService = authorCardService;
        this.archiveService = archiveService;

        scanController = new ScanController(scanService);
        _scanService = scanService;
        _history = scanService.History;
        FilterBar = new FilterBarViewModel(settings, savePathService.Profile);
        LiveryEntry.IsFindInGameAvailable = savePathService.Profile.MyDesigns is not null;
        Status = new GalleryStatusViewModel();
        Gallery = new GalleryViewModel(favoriteService, lockService);
        TagsBar = new TagsBarViewModel(Gallery.TagState, Gallery.SetTagState);
        Gallery.SelectedTagsChanged += TagsBar.SyncSelection;
        Update = new UpdateViewModel(updateService);
        Gallery.CountsUpdated += snapshot =>
        {
            Status.UpdateCountsAndEmptyState(snapshot.FilteredEntries, snapshot.TotalCount, snapshot.SearchText);
            UpdateSlots();
        };
        Gallery.SelectedEntries.CollectionChanged += (_, __) => RefreshArchiveAvailability();
        Gallery.InstalledCountsChanged += RefreshArchiveAvailability;
        Gallery.LocksChanged += RefreshArchiveAvailability;
        FilterBar.FiltersChanged += RefreshGallery;
        RefreshCommand = new AsyncRelayCommand(RefreshButtonClickedAsync);
        InitializeNavigation();
    }

    private GalleryFilterState CurrentFilterState() => new(
        FilterBar.SearchText, FilterBar.EffectiveSortMode, FilterBar.FavoriteMode, FilterBar.MineMode, FilterBar.EffectiveInstalledMode,
        FilterBar.DuplicatesFilterMode, FilterBar.GeneratedFilterMode, FilterBar.PaintFilterMode,
        FilterBar.EffectiveAuctionFilterMode, FilterBar.GroupingEnabled, settings.SearchByFolderName, FilterBar.ActiveQuickFilters);

    public void RefreshGallery() => Gallery.Refresh(CurrentFilterState());

    /// <summary>The row of tags again; true when a tag no livery has any more stopped filtering (the gallery is then shown again)</summary>
    public bool RebuildTagsBar()
    {
        TagsBar.RecomputeAllTags(Gallery.AllEntries);
        return Gallery.SyncKnownTags([.. TagsBar.AllTags.Select(t => t.Tag)]);
    }

    public void ApplyTagsEdit(LiveryEntry entry, List<string> newTags)
    {
        entry.Tags = newTags;
        tagService.SetTags(entry.FolderName, newTags);
        // The livery may leave (or join) what the tags filter; a tag nobody has any more stops filtering
        if (!RebuildTagsBar() && Gallery.HasTagFilter) RefreshGallery();
    }

    /// <summary>Several liveries at once: <paramref name="add"/> goes onto each, <paramref name="remove"/> off each</summary>
    public void ApplyTagsChange(IReadOnlyList<LiveryEntry> entries, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove)
    {
        bool changed = false;
        foreach (var entry in entries)
        {
            var tags = TagEdits.Change(entry.Tags, add, remove);
            if (tags.SequenceEqual(entry.Tags, StringComparer.Ordinal)) continue;
            entry.Tags = tags;
            tagService.SetTags(entry.FolderName, tags);
            changed = true;
        }
        if (!changed) return;
        if (!RebuildTagsBar() && Gallery.HasTagFilter) RefreshGallery();
    }

    /// <summary>
    /// The card of a livery's author: the one the user assigned it to, else its account's card, else a card made on the
    /// fly for its account (not saved until the user changes it). Null when nothing tells the author
    /// </summary>
    public AuthorCard? FindAuthorCard(LiveryEntry entry)
    {
        if (entry.AssignedCardId is { } assigned && authorCardService.FindCard(assigned) is { } card) return card;
        if (!AuthorAccounts.IsAccount(entry.AuthorIdentityTagHex)) return null;
        var names = new AuthorInsights.AuthorNames(Gallery.AllEntries, authorCardService);
        return authorCardService.GetCardForAccount(entry.AuthorIdentityTagHex!, names.NamesOf(entry.AuthorIdentityTagHex!));
    }

    /// <summary>The names authors are shown under, for the author dialogs</summary>
    public AuthorInsights.AuthorNames AuthorNames() => new(Gallery.AllEntries, authorCardService);

    /// <summary>
    /// Every author a livery can be assigned to: the cards of this game's authors and those without an account, and
    /// the accounts without a card (as cards made on the fly). By name
    /// </summary>
    public List<AuthorCard> AuthorChoices()
    {
        var names = AuthorNames();
        var tagsHere = Gallery.AllEntries.Select(e => e.AuthorIdentityTagHex).Where(AuthorAccounts.IsAccount)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignedHere = Gallery.AllEntries.Select(e => e.AssignedCardId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var cards = authorCardService.GetAll()
            .Where(c => c.IdentityTags.Count == 0 || c.IdentityTags.Any(tagsHere.Contains) || assignedHere.Contains(c.Id))
            .ToList();
        var carded = cards.SelectMany(c => c.IdentityTags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tagsHere)
            if (!carded.Contains(tag) && authorCardService.FindCardForIdentityTag(tag) is null)
                cards.Add(AuthorCard.ForAccount(tag, names.NamesOf(tag)));
        return [.. cards.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Assigns the livery to the author (the user is sure): a card made on the fly is saved first. False when the card
    /// could not be saved or is gone
    /// </summary>
    public Task<bool> AssignAuthorAsync(LiveryEntry entry, AuthorCard card) => AssignAuthorAsync([entry], card);

    /// <summary>
    /// Assigns several liveries to one author at once (the user is sure), with one save; liveries without a file hash
    /// are left out. False when the card could not be saved or is gone, or nothing could be assigned
    /// </summary>
    public async Task<bool> AssignAuthorAsync(IReadOnlyList<LiveryEntry> entries, AuthorCard card)
    {
        var liveries = entries
            .Where(e => e.CLiveryHash is not null)
            .Select(e => new AuthorAssignmentTarget(e.CLiveryHash!, e.Data.AuthorIdentityTagHex, e.Data.LiveryName))
            .ToList();
        if (liveries.Count == 0) return false;
        var target = card;
        if (card.IsImplicit)
        {
            target = card with { Id = Guid.NewGuid().ToString("N"), IsImplicit = false };
            if (!await authorCardService.TrySave(target)) return false;
        }
        if (!await authorCardService.AssignManyAsync(liveries, target.Id)) return false;
        await RefreshAfterAuthorsChangedAsync();
        Gallery.RefreshAuthorMenu();
        return true;
    }

    /// <summary>Forgets the author the user set for the livery</summary>
    public Task UnassignAuthorAsync(LiveryEntry entry) => UnassignAuthorAsync([entry]);

    /// <summary>Forgets the author the user set for each of the liveries, with one save</summary>
    public async Task UnassignAuthorAsync(IReadOnlyList<LiveryEntry> entries)
    {
        var hashes = entries.Select(e => e.CLiveryHash).OfType<string>().ToList();
        if (hashes.Count > 0 && await authorCardService.UnassignManyAsync(hashes))
        {
            await RefreshAfterAuthorsChangedAsync();
            Gallery.RefreshAuthorMenu();
        }
    }

    /// <summary>The user said a hint is wrong: it is not given again</summary>
    public async Task DismissAuthorHintAsync(AuthorHint hint)
    {
        await authorCardService.DismissHintAsync(hint.Key);
        AuthorInsights.Apply(Gallery.AllEntries, authorCardService);
    }

    /// <summary>The hints for a card without an account: accounts that may be its author's</summary>
    public IReadOnlyList<AuthorHintView> AccountHintsFor(AuthorCard card)
    {
        if (card.IdentityTags.Count > 0 || card.IsImplicit) return [];
        var entries = Gallery.AllEntries;
        var inputs = entries.Select(e => AuthorInsights.ToInput(e, authorCardService)).ToList();
        var hints = AuthorHints.ForCardsWithoutAccounts(inputs, [card], authorCardService.GetDismissedHints());
        if (!hints.TryGetValue(card.Id, out var found)) return [];
        var names = AuthorNames();
        var byKey = AuthorInsights.ByKey(entries);
        return [.. found.Select(h => new AuthorHintView(h, names.Of(h.SubjectId), $"{names.Of(h.SubjectId)}: {AuthorInsights.Reason(h, byKey)}"))];
    }

    /// <summary>Adds an account to a card without one (the user accepted a hint)</summary>
    public async Task<bool> AddAccountToCardAsync(AuthorCard card, string identityTag)
    {
        var names = AuthorNames().NamesOf(identityTag);
        var updated = card with
        {
            IdentityTags = [.. card.IdentityTags, identityTag],
            KnownNames = [.. card.KnownNames.Concat(names).Distinct(StringComparer.OrdinalIgnoreCase)],
        };
        return await TrySaveAuthorCard(updated);
    }

    public async Task<bool> TrySaveAuthorCard(AuthorCard card)
    {
        if (!await authorCardService.TrySave(card)) return false;
        await RefreshAfterAuthorsChangedAsync();
        return true;
    }

    /// <summary>What the statistics window reads from the gallery and asks it to do</summary>
    /// <param name="openAuthor">Opens a livery's author window over the statistics window</param>
    /// <param name="backups">The backups' manifests</param>
    public StatisticsHost StatisticsHost(Action<LiveryEntry> openAuthor, Func<IReadOnlyList<LiveryBackupManifest?>>? backups) => new(
        All: () => Gallery.AllEntries,
        Shown: Gallery.GetFilteredEntries,
        History: ReadHistory,
        Source: _statisticsSource ??= new StatisticsSource(authorCardService, () => _scanService.Authors.Own),
        IsInstalledKnown: () => Gallery.IsInstalledDataKnown,
        Navigate: NavigateFromStatistics,
        ApplyQuickFilter: FilterBar.ApplyQuickFilter,
        SelectTag: tag => SetTagSelected(tag, true),
        OpenAuthor: openAuthor)
    {
        MaxLiveries = savePathService.Profile.MaxLiveries,
        Backups = backups,
        Archive = () => [.. archiveService.GetAll().Where(a =>
            a.SourceAccount is null || string.Equals(a.SourceAccount.Key, _appliedAccountKey, StringComparison.OrdinalIgnoreCase))],
        GameCars = () => [.. carDatabase.GetAll().Select(c => (c.Id, c.Manufacturer))],
        ManufacturerLabel = ManufacturerLabel,
        Accounts = AccountSummariesAsync,
    };

    private Dictionary<string, string>? _manufacturerLabels;

    /// <summary>A manufacturer in the language the car names are shown in (the texts of one of its cars)</summary>
    private string? ManufacturerLabel(string raw)
    {
        if (_manufacturerLabels is null)
        {
            _manufacturerLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var car in carDatabase.GetAll())
                if (!_manufacturerLabels.ContainsKey(car.Manufacturer) && carTexts.For(car.Id)?.Texts.Manufacturer is { Length: > 0 } label)
                    _manufacturerLabels[car.Manufacturer] = label;
        }
        return _manufacturerLabels.GetValueOrDefault(raw);
    }

    /// <summary>Every account this PC keeps data of, at a glance: its liveries from its scan cache (the shown one from the gallery)</summary>
    private async Task<IReadOnlyList<AccountSummary>> AccountSummariesAsync()
    {
        var stored = await ListStoredAccountsAsync();
        var profile = savePathService.Profile;
        var current = Gallery.AllEntries;
        return await Task.Run(() => (IReadOnlyList<AccountSummary>)[.. stored.Select(account =>
        {
            if (account.IsCurrent)
                return new AccountSummary(account.Title, true, current.Count(e => !e.IsAuction),
                    current.Select(e => e.AuthorIdentityTagHex).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    current.Max(e => e.DownloadDate));
            var entries = _scanService.CacheOf(account.Key);
            return new AccountSummary(account.Title, false, entries.Count(e => !profile.IsAuction(e.FolderName)),
                entries.Select(e => e.AuthorIdentityTagHex).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                entries.Max(e => e.DownloadDate));
        })]);
    }

    private StatisticsSource? _statisticsSource;

    /// <summary>
    /// The author window's numbers for one author's liveries: the same calculation as the statistics window's authors'
    /// table (liveries, cars, favorites, the first and the latest download)
    /// </summary>
    public ForzaToolkit.Liveries.Statistics.AuthorStatisticsRow? AuthorStatisticsFor(IReadOnlyList<LiveryEntry> entries)
    {
        if (entries.Count == 0) return null;
        const string Author = "author";
        return ForzaToolkit.Liveries.Statistics.AuthorStatisticsCalculator.Calculate(entries, _ => Author).Rows.FirstOrDefault();
    }

    /// <summary>The journal of the account shown; empty without one</summary>
    public IReadOnlyList<ForzaToolkit.Liveries.History.LiveryHistoryEvent> ReadHistory() =>
        _history is not null && _appliedAccountKey is { } key ? _history.Read(key) : [];

    /// <summary>A statistics tile or row: the gallery shows what it counted</summary>
    private void NavigateFromStatistics(StatisticsTarget target)
    {
        switch (target)
        {
            case StatisticsTarget.Everything:
                FilterBar.ShowEverything();
                Gallery.ClearTagFilter();
                break;
            case StatisticsTarget.ByAuthor:
                FilterBar.SortMode = SortMode.Author;
                FilterBar.GroupingEnabled = true;
                break;
            case StatisticsTarget.ByManufacturer:
                FilterBar.SortMode = SortMode.Manufacture;
                FilterBar.GroupingEnabled = true;
                break;
            case StatisticsTarget.Favorites:
                FilterBar.FavoriteMode = FavoriteMode.OnlyFavorites;
                break;
            case StatisticsTarget.Installed:
                FilterBar.InstalledMode = InstalledMode.OnlyInstalled;
                break;
            case StatisticsTarget.Auction:
                FilterBar.AuctionFilterMode = AuctionFilterMode.OnlyAuction;
                break;
            case StatisticsTarget.Mine:
                FilterBar.MineMode = MineMode.OnlyMine;
                break;
            case StatisticsTarget.Generated:
                FilterBar.GeneratedFilterMode = GeneratedFilterMode.GeneratedOnly;
                break;
            case StatisticsTarget.Duplicates:
                FilterBar.DuplicatesFilterMode = DuplicatesFilterMode.DuplicatesAndPossible;
                break;
            case StatisticsTarget.PaintOnly:
                FilterBar.PaintFilterMode = PaintFilterMode.PaintOnly;
                break;
        }
    }

    private void OnEntriesChanged()
    {
        if (!RebuildTagsBar()) RefreshGallery();
    }

    public async Task RunScanAsyncTracked(bool isUserInitiated) => await RunScanCoreAsync(isUserInitiated);

    public Task<bool> RunAutoRefreshAsync(CancellationToken ct = default) => RunScanCoreAsync(isUserInitiated: false, ct);

    /// <summary>
    /// A scan that makes the previews the gallery has just drawn the liveries' previews (<see cref="RenderedThumbnails"/>),
    /// whether or not automatic refresh is on. False when another scan was running: ask again after it.
    /// </summary>
    public Task<bool> RunScanForDrawnPreviewsAsync(CancellationToken ct = default) =>
        RunScanCoreAsync(isUserInitiated: false, ct, evenWithoutAutoRefresh: true);

    public event Action? SavePathChanged;

    private async Task<bool> RunScanCoreAsync(bool isUserInitiated, CancellationToken ct = default, bool evenWithoutAutoRefresh = false)
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

        if (!isUserInitiated && !evenWithoutAutoRefresh && !settings.AutoRefreshLiveries) return true;

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
            CheckRoomOnce();
            _ = BackUpAfterScanAsync();

            _changesSinceLastLaunchChecked = true;
            if (result.ChangesSinceLastLaunch is { IsEmpty: false } changes) ChangesSinceLastLaunchFound?.Invoke(changes);
        }

        if (scanError is not null)
        {
            AppLog.LogErrorThrottled(saveDataPath, $"Scan failed: '{saveDataPath}'", scanError);
            if (isUserInitiated)
                Status.SetStatusText(string.Format(Strings.StatusScanError, scanError.Message));
        }
        return true;
    }

    /// <summary>
    /// After the user changed who liveries are by (a livery assigned to an author or taken off, a card saved, merged,
    /// split or deleted): the entries from the cache at once, then the duplicates looked for again. Duplicates are looked
    /// for among one author's liveries on one car, so new authors make new groups, and the cache's statuses were made
    /// with the old ones: a livery of an unknown author assigned to an author was not checked against his liveries until
    /// the next scan. The scan reuses everything it parsed; the authors' signature it keeps tells it to compare again
    /// </summary>
    public async Task RefreshAfterAuthorsChangedAsync()
    {
        await RefreshEntriesFromCacheAsync();
        // A scan already running may have taken the authors before the change: ask again once it is done
        for (int attempt = 0; attempt < 2; attempt++)
            if (await RunScanCoreAsync(isUserInitiated: false, evenWithoutAutoRefresh: true)) return;
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
        AuthorInsights.Apply(Gallery.AllEntries, authorCardService);
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
        ArchiveRules.Check(entry.IsLocked, entry.IsAuction, entry.IsInstalled, Gallery.IsInstalledDataKnown);

    private bool CanArchive(LiveryEntry entry) => ArchiveBlockOf(entry) == ArchiveBlock.None;

    private bool CanMoveSelectedToArchive() => Gallery.SelectedEntries.Any(CanArchive);

    private void RefreshArchiveAvailability()
    {
        MoveSelectedToArchiveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ArchiveBlockedTooltip));
    }

    public string? ArchiveBlockedTooltip =>
        Gallery.HasSelection && !CanMoveSelectedToArchive() ? ArchiveBlockedReason(Gallery.SelectedEntries) : null;

    /// <summary>Why none of <paramref name="blocked"/> can go: one line per kind of block among them</summary>
    private string ArchiveBlockedReason(IEnumerable<LiveryEntry> blocked)
    {
        bool anyLocked = false, anyAuction = false, anyInstalled = false;
        foreach (var entry in blocked)
        {
            switch (ArchiveBlockOf(entry))
            {
                case ArchiveBlock.Locked: anyLocked = true; break;
                case ArchiveBlock.AuctionNeedsInstalledList: anyAuction = true; break;
                case ArchiveBlock.Installed: anyInstalled = true; break;
            }
        }
        string? other = (anyAuction, anyInstalled) switch
        {
            (true, true) => Strings.ArchiveAuctionOrInstalledNotAllowedTooltip,
            (false, true) => Strings.ArchiveInstalledNotAllowedTooltip,
            (true, false) => Strings.ArchiveAuctionNotAllowedTooltip,
            _ => null,
        };
        return (anyLocked, other) switch
        {
            (true, null) => Strings.ArchiveLockedNotAllowedTooltip,
            (true, _) => Strings.ArchiveLockedNotAllowedTooltip + Environment.NewLine + other,
            _ => other ?? Strings.ArchiveAuctionNotAllowedTooltip,
        };
    }

    private async Task<bool> ConfirmArchiveRiskAsync(IReadOnlyList<LiveryEntry> entries)
    {
        int CountToArchive() => entries.Count(CanArchive);

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
            AppLog.LogError("Failed to check whether the installed liveries are up to date", ex);
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
            AppLog.LogError("Failed to check whether the installed liveries are up to date", ex);
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
        await MoveToArchiveAsync(Gallery.SelectedEntries.ToList());
    }

    /// <summary>
    /// The gallery's automatic backups; set by the window. Asked before liveries go to the archive and after each scan
    /// </summary>
    public AutoBackups? AutoBackups { get; set; }

    /// <summary>Asked when the backup before an archive could not be written; true to archive without it</summary>
    public Func<Task<bool>>? ConfirmArchiveWithoutBackupAsync { get; set; }

    /// <summary>
    /// Moves <paramref name="entries"/> to the archive with every check of the context menu's command: the warnings
    /// about the list of applied liveries, the running game, the automatic backup before it, and what was skipped and why.
    /// The selection or "free up room" asks for it
    /// </summary>
    /// <returns>True when liveries went</returns>
    public async Task<bool> MoveToArchiveAsync(IReadOnlyList<LiveryEntry> entries)
    {
        if (entries.Count == 0) return false;
        string? savePath = savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            ErrorMessageRequested?.Invoke(Strings.ArchiveNoSavePathMessage);
            return false;
        }

        if (!entries.Any(CanArchive))
        {
            ErrorMessageRequested?.Invoke(ArchiveBlockedReason(entries));
            return false;
        }

        if (!await ConfirmArchiveRiskAsync(entries)) return false;
        if (!await savePathService.Profile.RunningGuard.ConfirmAsync(ConfirmWhileGameRunningAsync)) return false;
        var selected = entries.ToList();

        // An auction livery goes only when the list of applied liveries is from the current save: a car may have
        // been given it since the last sync. Regular liveries keep the warning above, which the user can turn off
        bool auctionAllowed = !selected.Any(e => e.IsAuction && CanArchive(e)) || await IsInstalledListCurrentAsync();
        if (ShutdownToken.IsCancellationRequested) return false;

        var archivable = selected.Where(e => CanArchive(e) && (!e.IsAuction || auctionAllowed)).ToList();
        var toArchive = archivable.Select(e => e.Data).ToList();
        // Each skipped livery is counted once, under the first reason: the lock, then the car, then the auction
        int skippedLocked = selected.Count(e => e.IsLocked);
        int skippedInstalled = selected.Count(e => !e.IsLocked && e.IsInstalled);
        int skippedAuction = selected.Count(e => !e.IsLocked && e.IsAuction && !e.IsInstalled && !archivable.Contains(e));
        if (toArchive.Count == 0)
        {
            if (skippedAuction > 0 && skippedInstalled == 0 && skippedLocked == 0 && Gallery.IsInstalledDataKnown)
                ErrorMessageRequested?.Invoke(string.Format(Strings.ArchiveAuctionSkippedFormat, skippedAuction));
            else if (selected.Count > 0)
                ErrorMessageRequested?.Invoke(ArchiveBlockedReason(selected));
            return false;
        }

        if (!await BackUpBeforeArchiveAsync(savePath)) return false;

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
            AppLog.LogError("Failed to move selected liveries to archive", ex);
            ErrorMessageRequested?.Invoke(Strings.FileOperationFailedMessage);
            return false;
        }
        finally
        {
            Status.SetLoading(false);
        }

        var notes = new List<string>(4);
        if (skippedLocked > 0) notes.Add(string.Format(Strings.ArchiveLockedSkippedFormat, skippedLocked));
        if (skippedAuction > 0) notes.Add(string.Format(Strings.ArchiveAuctionSkippedFormat, skippedAuction));
        if (skippedInstalled > 0) notes.Add(string.Format(Strings.ArchiveInstalledSkippedFormat, skippedInstalled));
        if (result.LeftoverPaths.Count > 0)
            notes.Add(string.Format(Strings.ArchiveLeftoverFoldersFormat, string.Join(Environment.NewLine, result.LeftoverPaths)));
        if (notes.Count > 0) ErrorMessageRequested?.Invoke(string.Join(Environment.NewLine + Environment.NewLine, notes));
        return result.Completed.Count > 0;
    }

    /// <summary>
    /// The automatic backup before an archive, when it is on and the newest backup does not already hold the save.
    /// False when it failed and the user would rather not archive without it
    /// </summary>
    private async Task<bool> BackUpBeforeArchiveAsync(string savePath)
    {
        if (AutoBackups is not { } auto) return true;
        Status.SetLoading(true, Strings.LoadingBackupBeforeArchive);
        AutoBackupOutcome outcome;
        try
        {
            outcome = await auto.RunAsync(AutoBackupMoment.BeforeArchive, [.. Gallery.AllEntries.Select(e => e.Data)], savePath,
                GetAccountContext().Current);
        }
        finally
        {
            Status.SetLoading(false);
        }
        if (outcome != AutoBackupOutcome.Failed) return true;
        return ConfirmArchiveWithoutBackupAsync is { } ask && await ask();
    }

    /// <summary>After a scan: the backup after a change of the save, or the scheduled one, in the background</summary>
    private async Task BackUpAfterScanAsync()
    {
        if (AutoBackups is not { } auto || savePathService.ResolveSaveDataPath() is not { } savePath) return;
        var entries = Gallery.AllEntries.Select(e => e.Data).ToList();
        try
        {
            if (await auto.RunAsync(AutoBackupMoment.AfterScan, entries, savePath, GetAccountContext().Current) == AutoBackupOutcome.Made)
                Status.SetStatusText(Strings.StatusAutoBackupMade);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Automatic backup after a scan failed", ex);
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
            AppLog.LogError(
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
            AppLog.LogError($"Background work '{what}' failed while shutting down", ex);
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
            AppLog.LogError($"Unhandled exception in background task: {context}", ex);
        }
    }
}
