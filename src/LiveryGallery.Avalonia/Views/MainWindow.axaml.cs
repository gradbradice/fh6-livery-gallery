using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveryGallery.Controller;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.ViewModels;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Views;

internal partial class MainWindow : Window
{
    private readonly GameProfile _profile;
    private readonly AuthorCardService _authorCardService;
    private readonly LiveryArchive _archiveService;
    private readonly LiveryBackups _backupService;
    private readonly ILivery3DViewer _viewer3D;
    private readonly IGameFolderCheck _gameFolderCheck;
    private readonly AboutComponents _aboutComponents;
    private readonly DiagnosticsExportService _diagnosticsService;
    private MainViewModel _mainViewModel = null!;
    private SavePathService _savePathService = null!;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AppSettingsData _settings;
    private readonly SaveFolderWatcher _saveFolderWatcher;
    private readonly ServerConnectionController _serverConnection;
    private bool _isLoaded;
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _resizeDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly BackgroundTaskTracker _uiTasks = new();
    private readonly SaveFolderLock _saveFolderLock;
    private static readonly TimeSpan ShutdownStepTimeout = TimeSpan.FromSeconds(5);

    public MainWindow(
        AppSettingsData settings,
        GameProfile profile,
        ILiveryServerClient serverClient,
        CarDatabaseService carDatabase,
        CarTextProvider carTexts,
        TagService tagService,
        FavoriteService favoriteService,
        LockService lockService,
        AuthorCardService authorCardService,
        LiveryArchive archiveService,
        LiveryBackups backupService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService,
        ILivery3DViewer viewer3D,
        IGameFolderCheck gameFolderCheck,
        AboutComponents aboutComponents,
        SaveFolderLock saveFolderLock,
        DiagnosticsExportService diagnosticsService,
        AccountRegistry accountRegistry)
    {
        InitializeComponent();
        InitializeShortcuts();

        _saveFolderLock = saveFolderLock;

        _settings = settings;
        _profile = profile;
        _authorCardService = authorCardService;
        _archiveService = archiveService;
        _backupService = backupService;
        _viewer3D = viewer3D;
        _gameFolderCheck = gameFolderCheck;
        _aboutComponents = aboutComponents;
        _diagnosticsService = diagnosticsService;
        _savePathService = new SavePathService(settings, profile);
        var savePathPrompter = new SavePathPrompter(this, profile);
        // A livery without a preview in the save gets one drawn from the 3D model, kept with the game's previews (never
        // in the save); a scan then makes it the livery's preview, which the archive and backups take along
        var rendered = new RenderedThumbnails(viewer3D.Thumbnails,
            folderName => _savePathService.ResolveSaveDataPath() is { } saveData ? _profile.Liveries.GetFiles(saveData, folderName).LiveryPath : null,
            Configuration.AppSettings.ThumbsPath, saveFolderLock);
        rendered.Drawn += () => { _drawnPreviewsScanTimer.Stop(); _drawnPreviewsScanTimer.Start(); };
        _drawnPreviewsScanTimer.Tick += (_, _) =>
        {
            _drawnPreviewsScanTimer.Stop();
            RunUiTask(ScanForDrawnPreviewsAsync, "scan for drawn previews");
        };
        ThumbnailLifecycleController.Rendered = rendered;

        _mainViewModel = new MainViewModel(
            settings, savePathPrompter, _savePathService, carDatabase, carTexts, tagService, authorCardService,
            favoriteService, lockService, archiveService, scanService, updateService, accountRegistry)
        {
            ShutdownToken = _shutdownCts.Token
        };

        DataContext = _mainViewModel;
        _saveFolderWatcher = new SaveFolderWatcher(ct => _mainViewModel.RunAutoRefreshAsync(ct), profile);
        _mainViewModel.SavePathChanged += SyncSaveFolderWatcher;

        _serverConnection = new ServerConnectionController(
            serverClient,
            new ProfileDataLocator(profile.Saves),
            new InstalledLiveriesCache(Path.Combine(Configuration.AppSettings.BaseCachePath, InstalledLiveriesCache.DefaultFileName)),
            _mainViewModel.ServerStatus,
            () => _savePathService.ResolveSaveDataPath(),
            counts =>
            {
                _mainViewModel.Gallery.SetInstalledLiveries(counts);
                if (_mainViewModel.FilterBar.EffectiveInstalledMode != InstalledMode.None) _mainViewModel.RefreshGallery();
            });
        _mainViewModel.ScanCompleted += _serverConnection.RequestSync;
        _mainViewModel.SavePathChanged += _serverConnection.OnSavePathChanged;
        _saveFolderWatcher.ProfileChanged += _serverConnection.RequestSync;
        Activated += (_, __) =>
        {
            _serverConnection.OnWindowActivated();
            ReloadAuthorCardsIfChanged();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                _serverConnection.SetWindowVisible(WindowState != WindowState.Minimized);
        };
        _mainViewModel.Gallery.AuthorRowRequested += entry => RunUiTask(() => ShowAuthorRowAsync(entry), "author card");
        _mainViewModel.Gallery.EditTagsRequested += entries => RunUiTask(() => ShowEditTagsAsync(entries), "edit tags");
        // From the selection: the selected liveries left out (an account's id) are counted in the dialog
        _mainViewModel.Gallery.AssignAuthorRequested += entries => RunUiTask(() => AssignAuthorAsync(entries, null,
            entries.All(e => e.IsSelected) ? _mainViewModel.Gallery.SelectedCount - entries.Count : 0), "assign author");
        _mainViewModel.Gallery.UnassignAuthorRequested += entries => RunUiTask(() => _mainViewModel.UnassignAuthorAsync(entries), "unassign author");
        _mainViewModel.Gallery.ViewPreviewRequested += entry => RunUiTask(() => ShowPreviewAsync(entry), "preview");
        _mainViewModel.Gallery.View3DRequested += entry =>
            RunUiTask(() => ShowLivery3DAsync(entry), $"3D viewer for '{entry.FolderName}'");
        _viewer3D.StateChanged += OnViewer3DStateChanged;
        _mainViewModel.ChangesSinceLastLaunchFound += changes =>
            RunUiTask(() => ShowChangesSinceLastLaunchAsync(changes), "changes since last launch");
        _mainViewModel.ErrorMessageRequested += message =>
            RunUiTask(() => InfoDialog.ShowAsync(this, Configuration.AppSettings.Identity.Name, message), "error message");
        _mainViewModel.ConfirmArchiveWithoutInstalledDataAsync = count => AskArchiveWarningAsync(
            count == 1 ? Strings.ArchiveInstalledUnknownMessage : Strings.ArchiveInstalledUnknownMessageMany);
        _mainViewModel.ConfirmArchiveWithOutdatedInstalledDataAsync = count => AskArchiveWarningAsync(
            count == 1 ? Strings.ArchiveInstalledOutdatedMessage : Strings.ArchiveInstalledOutdatedMessageMany);
        _mainViewModel.IsInstalledDataOutdatedAsync = () => _serverConnection.IsInstalledResultOutdatedAsync(_shutdownCts.Token);
        _mainViewModel.ConfirmWhileGameRunningAsync = () => ConfirmDialog.AskAsync(
            this, string.Format(Strings.GameRunningTitleFormat, _profile.Game.Name), Strings.GameRunningFileChangeMessage, Strings.GameRunningContinueButton, Strings.ButtonCancel);
        _mainViewModel.GameFolderStateProvider = () => new GameFolderState(_viewer3D.IsChecking, _viewer3D.GameDirectory);
        InitializeRoom();

        GalleryPanel.AnchorKeySelector = GetGalleryAnchorKeys;
        _mainViewModel.Gallery.GroupsReplaced += () => RebuildGalleryItems(preservePosition: true);

        ApplyLocalizedTexts();

        _searchDebounceTimer.Tick += (_, __) =>
        {
            _searchDebounceTimer.Stop();
            RefreshGallery();
        };

        _resizeDebounceTimer.Tick += (_, __) =>
        {
            _resizeDebounceTimer.Stop();
            if (_isLoaded) UpdateGroupWidthsOnly();
        };

        SizeChanged += (_, __) =>
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        };

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        AppLocalisationService.LanguageChanged += OnLanguageChanged;
    }

    private bool _isClosing;
    // The game folder the drawn previews on screen were drawn with (ThumbnailLifecycleController.Rendered)
    private string? _renderedThumbnailsGameDirectory;
    // Drawn previews become the liveries' previews with a scan, one for a run of them
    private readonly DispatcherTimer _drawnPreviewsScanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _shutdownCompleted;
    private Task? _initializeTask;
    private Task? _shutdownTask;

    private void RunUiTask(Func<Task> work, string context) => _uiTasks.Run(work, context);

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownCompleted) return;
        e.Cancel = true;
        _shutdownTask ??= ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            await ShutdownAsync();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Shutdown failed; closing anyway", ex);
        }
        finally
        {
            AppLog.Shutdown();
            _shutdownCompleted = true;
            Close();
        }
    }

    private async Task ShutdownAsync()
    {
        _isClosing = true;
        _shutdownCts.Cancel();
        _viewer3D.StateChanged -= OnViewer3DStateChanged;
        AppLocalisationService.LanguageChanged -= OnLanguageChanged;
        _searchDebounceTimer.Stop();
        _resizeDebounceTimer.Stop();
        _drawnPreviewsScanTimer.Stop();
        ThumbnailLifecycleController.BeginShutdown();
        _viewer3D.BeginShutdown();

        await AwaitShutdownStepAsync(_saveFolderWatcher.StopAsync(), "save folder watcher", ShutdownStepTimeout);
        await AwaitShutdownStepAsync(_serverConnection.StopAsync(), "livery server connection", ShutdownStepTimeout);
        await AwaitShutdownStepAsync(_initializeTask, "initialization", ShutdownStepTimeout);
        await _uiTasks.WaitAllAsync(ShutdownStepTimeout);
        await AwaitShutdownStepAsync(_saveFolderLock.CloseAsync(), "save folder operations", Timeout.InfiniteTimeSpan);
        await AwaitShutdownStepAsync(_mainViewModel.ShutdownAsync(), "gallery", Timeout.InfiniteTimeSpan);
        await AwaitShutdownStepAsync(_viewer3D.ShutdownAsync(ShutdownStepTimeout), "3D renderer", Timeout.InfiniteTimeSpan);
    }

    private static async Task AwaitShutdownStepAsync(Task? step, string name, TimeSpan timeout)
    {
        if (step is null) return;
        try
        {
            await step.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLog.LogWarning($"Shutdown: '{name}' did not finish within {timeout.TotalSeconds:0.#} s, continuing without it");
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the shutdown itself
        }
        catch (Exception ex)
        {
            AppLog.LogError($"Shutdown: '{name}' failed", ex);
        }
    }

    private void UpdateGroupWidthsOnly()
    {
        double width = ComputeGroupWidth();
        if (_mainViewModel.Gallery.GroupWidth == width) return;
        _mainViewModel.Gallery.GroupWidth = width;
        RebuildGalleryItems(preservePosition: true);
    }

    private readonly HashSet<string> _collapsedGroupKeys = new(StringComparer.Ordinal);

    private void RebuildGalleryItems(bool preservePosition)
    {
        var items = new List<object>();
        foreach (var group in _mainViewModel.Gallery.DisplayedGroups)
        {
            group.IsExpanded = !_collapsedGroupKeys.Contains(group.Key);
            items.Add(group);
            if (group.IsExpanded) items.AddRange(group.Rows);
        }
        GalleryPanel.SetItems(items, preservePosition);
    }

    private static IEnumerable<string> GetGalleryAnchorKeys(object item) => item switch
    {
        GalleryRow row => row.Items.Select(entry => entry.FolderName),
        LiveryGroup group => ["group:" + group.Key],
        _ => []
    };

    private void GroupShowOnlyButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: LiveryGroup { GroupFilter: { } filter } })
            _mainViewModel.ApplyQuickFilter(filter);
    }

    private void QuickFilterPickerFlyout_Opening(object? sender, EventArgs e) =>
        _mainViewModel.PrepareQuickFilterPicker();

    private void QuickFilterPickerFlyout_Opened(object? sender, EventArgs e)
    {
        QuickFilterOptionsList.SelectedItem = null;
        QuickFilterSearchBox.Focus();
    }

    private void QuickFilterSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        var options = _mainViewModel.QuickFilterPicker.Options;
        if (e.Key == Key.Enter && options.Count > 0)
        {
            e.Handled = true;
            ApplyPickedQuickFilter(options[0]);
        }
        else if (e.Key == Key.Down && options.Count > 0)
        {
            e.Handled = true;
            QuickFilterOptionsList.SelectedIndex = 0;
            QuickFilterOptionsList.ContainerFromIndex(0)?.Focus();
        }
    }

    private void QuickFilterOptionsList_Tapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is QuickFilterOption option)
            ApplyPickedQuickFilter(option);
    }

    private void QuickFilterOptionsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && QuickFilterOptionsList.SelectedItem is QuickFilterOption option)
        {
            e.Handled = true;
            ApplyPickedQuickFilter(option);
        }
    }

    private void ApplyPickedQuickFilter(QuickFilterOption option)
    {
        _mainViewModel.ApplyQuickFilter(option.Filter);
        if (option.Filter.Kind == QuickFilterKind.Manufacturer)
        {
            _mainViewModel.QuickFilterPicker.ShowCarsOf(option.Filter);
            QuickFilterOptionsList.SelectedItem = null;
            QuickFilterSearchBox.Focus();
            return;
        }
        AddQuickFilterButton.Flyout?.Hide();
    }

    private void GroupHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LiveryGroup group }) return;

        if (group.IsExpanded) _collapsedGroupKeys.Remove(group.Key);
        else _collapsedGroupKeys.Add(group.Key);
        RebuildGalleryItems(preservePosition: true);
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e) => RunUiTask(OnLoadedAsync, "startup");

    private async Task OnLoadedAsync()
    {
        _isLoaded = true;
        UpdateGroupWidthsOnly();
        if (_mainViewModel.WasSavedPathMissing)
            await InfoDialog.ShowAsync(this, Strings.FolderNotFoundTitle, Strings.SavedPathNotFoundNotice);
        if (_isClosing) return;

        RunUiTask(ApplyGamePathAsync, "game folder");
        await JoinOldAuthorCardsAsync();
        if (_isClosing) return;
        _initializeTask = _mainViewModel.InitializeAsync(_shutdownCts.Token);
        await _initializeTask;
        if (_isClosing) return;
        SyncSaveFolderWatcher();
        ApplyServerConnectionSetting();
    }

    /// <summary>
    /// The cards this app kept in its own folder, when another ForzaToolkit app had already started the shared ones:
    /// joined before the first scan, so the scan shows their names
    /// </summary>
    private async Task JoinOldAuthorCardsAsync()
    {
        try
        {
            if (await SharedAuthorCards.CompleteMoveAsync(_authorCardService, Configuration.AppSettings.BaseCachePath) is { Skipped: > 0 } result)
                AppLog.LogWarning($"Author cards: {result.Skipped} cards of this app clash with cards of another app and stay only in the old file");
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to join this app's author cards into the shared ones", ex);
        }
    }

    /// <summary>Another ForzaToolkit app may have changed the shared author cards while this window was in the background</summary>
    private void ReloadAuthorCardsIfChanged()
    {
        if (_isClosing || _initializeTask is not { IsCompleted: true }) return;
        if (_authorCardService.ReloadIfChanged())
            RunUiTask(_mainViewModel.RefreshAfterAuthorsChangedAsync, "author cards changed elsewhere");
    }

    private void ApplyServerConnectionSetting()
    {
        if (_isClosing) return;
        // A game without the livery server has neither the connection nor the "installed" filter
        bool enabled = _settings.ServerConnectionEnabled && _profile.Features.LiveryServer;
        _mainViewModel.FilterBar.SetInstalledFilterAvailable(enabled);
        _serverConnection.Configure(enabled);
    }

    private void ServerStatusIndicator_Click(object? sender, RoutedEventArgs e) => _serverConnection.RecheckNow();

    private void SyncSaveFolderWatcher()
    {
        if (_isClosing) return;
        _saveFolderWatcher.Configure(_savePathService.SavePath, _settings.AutoRefreshLiveries);
    }

    private async Task ApplyGamePathAsync()
    {
        try
        {
            if (_isClosing) return;
            string? path = string.IsNullOrWhiteSpace(_settings.GameInstallPath)
                ? await _profile.FindInstallFolderAsync()
                : _settings.GameInstallPath;
            if (_isClosing) return;
            await _viewer3D.SetGamePathAsync(path);
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to apply the game folder", ex);
        }
    }

    private async Task ScanForDrawnPreviewsAsync()
    {
        if (_isClosing) return;
        // Another scan was running: it may have missed the new previews, so try again after it
        if (!await _mainViewModel.RunScanForDrawnPreviewsAsync(_shutdownCts.Token) && !_isClosing)
            _drawnPreviewsScanTimer.Start();
    }

    private void OnViewer3DStateChanged()
    {
        LiveryEntry.View3DGameFolderProblem = _viewer3D switch
        {
            { IsChecking: true } => Strings.View3DGameFolderChecking,
            { IsAvailable: true } => null,
            _ => string.Format(Strings.View3DGameFolderInvalidFormat,
                GameFolderText.FirstError(_viewer3D.FolderReport) ?? Strings.GameFolderProblemUnknown)
        };
        _mainViewModel.Gallery.RefreshView3DAvailability();
        if (!_viewer3D.IsChecking) _mainViewModel.RefreshGamePositions(); // a menu mod may change the rows
        if (!_viewer3D.IsChecking
            && !string.Equals(_renderedThumbnailsGameDirectory, _viewer3D.GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _renderedThumbnailsGameDirectory = _viewer3D.GameDirectory;
            ThumbnailLifecycleController.RefreshRenderedThumbnails();
        }
    }

    private async Task ShowLivery3DAsync(LiveryEntry entry)
    {
        if (!_viewer3D.IsAvailable)
        {
            await InfoDialog.ShowAsync(this, Strings.View3DDialogTitle, entry.View3DTooltip);
            return;
        }

        string? savePath = _savePathService.ResolveSaveDataPath();
        if (savePath is null)
        {
            await InfoDialog.ShowAsync(this, Strings.View3DDialogTitle, Strings.View3DReadFailed);
            return;
        }

        string liveryPath = _profile.Liveries.GetFiles(savePath, entry.FolderName).LiveryPath;
        var result = await _viewer3D.ShowAsync(this, new Livery3DRequest(entry.LiveryName, entry.CarId, liveryPath));
        if (_isClosing) return;
        string? problem = result.Problem switch
        {
            Livery3DProblem.NotAvailable => entry.View3DTooltip,
            Livery3DProblem.ReadFailed => Strings.View3DReadFailed,
            Livery3DProblem.ParseFailed => string.Format(Strings.View3DParseFailedFormat, result.Detail ?? "?"),
            _ => null,
        };
        if (problem is not null) await InfoDialog.ShowAsync(this, Strings.View3DDialogTitle, problem);
    }

    private Task<(bool Confirmed, bool DontShowAgain)> AskArchiveWarningAsync(string message) =>
        ConfirmDialog.AskWithOptionAsync(
            this, Strings.ContextMenuMoveToArchive, message,
            Strings.DontShowAgainCheckBox, Strings.ButtonYes, Strings.ButtonCancel);

    private async Task ShowChangesSinceLastLaunchAsync(LiveryChanges changes)
    {
        if (_isClosing) return;
        var dialog = new ChangesSinceLastLaunchDialog(changes);
        await dialog.ShowDialog(this);
        if (dialog.DontShowAgain) _mainViewModel.TurnOffChangesSinceLastLaunch();
    }

    private void UpdateBanner_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainViewModel.Update.LatestVersion is null) return;
        var dialog = new WhatsNewDialog(
            _mainViewModel.Update.LatestVersion, _mainViewModel.Update.ReleaseBody, _mainViewModel.Update.ReleaseUrl);
        RunUiTask(() => dialog.ShowDialog(this), "what's new dialog");
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void RefreshGallery() => _mainViewModel.RefreshGallery();

    private const double CardStep = 286;
    private const double Reserve = 32;

    private double ComputeGroupWidth()
    {
        double viewportWidth = GalleryScroll.Viewport.Width;
        if (viewportWidth <= 0)
            viewportWidth = ClientSize.Width;

        double usable = viewportWidth - Reserve;
        int columns = Math.Max(1, (int)(usable / CardStep));
        if ((columns + 1) * CardStep <= viewportWidth)
            columns += 1;

        return Math.Max(columns * CardStep, CardStep);
    }

    /// <param name="entry">A livery of the author</param>
    /// <param name="owner">The window the dialogs open over (the statistics window); the gallery when null</param>
    private async Task ShowAuthorRowAsync(LiveryEntry entry, Window? owner = null)
    {
        owner ??= this;
        // Every account has a card (made on the fly until the user changes it); a livery assigned by the user has its card
        if (_mainViewModel.FindAuthorCard(entry) is { } card)
        {
            await new AuthorCardViewDialog(card, AuthorCardActions()).ShowDialog(owner);
            return;
        }
        // Nothing tells the author (an edited header's id, or zeros): the user may say whose livery it is
        if (entry.CanAssignAuthor && entry.CLiveryHash is not null)
        {
            await AssignAuthorAsync(entry, owner);
            return;
        }
        await InfoDialog.ShowAsync(owner, Strings.AuthorCardViewNoCardTitle, Strings.AuthorUnknownMessage);
    }

    private AuthorCardViewActions AuthorCardActions() => new(
        Edit: EditAuthorCardAsync,
        Entries: () => _mainViewModel.Gallery.AllEntries,
        Unassign: async hash =>
        {
            if (_mainViewModel.Gallery.AllEntries.FirstOrDefault(e => e.CLiveryHash == hash) is { } entry)
                await _mainViewModel.UnassignAuthorAsync(entry);
        },
        AccountHints: _mainViewModel.AccountHintsFor,
        AddAccount: async (card, tag) => await _mainViewModel.AddAccountToCardAsync(card, tag) ? _authorCardService.FindCard(card.Id) : null,
        DismissHint: _mainViewModel.DismissAuthorHintAsync,
        FindCard: _authorCardService.FindCard,
        Statistics: _mainViewModel.AuthorStatisticsFor);

    /// <summary>The card editor over <paramref name="owner"/>; the saved card, or null when cancelled or refused</summary>
    private async Task<AuthorCard?> EditAuthorCardAsync(Window owner, AuthorCard? card)
    {
        var entries = _mainViewModel.Gallery.AllEntries;
        var accounts = AuthorContext.LabelAccounts(
            _authorCardService.BuildAccountIndex(entries.AuthorObservations(), card is { IsImplicit: false } ? card.Id : null),
            entries, _authorCardService);
        var dialog = new AuthorCardEditDialog(_authorCardService, accounts, card);
        bool saved = await dialog.ShowDialog<bool>(owner);
        if (!saved || dialog.Result is not { } result) return null;
        if (!await _mainViewModel.TrySaveAuthorCard(result))
        {
            await InfoDialog.ShowAsync(owner, Strings.AssignAuthorDialogTitle, Strings.AssignAuthorFailedMessage);
            return null;
        }
        return _authorCardService.FindCard(result.Id) ?? result;
    }

    /// <summary>"Whose livery is it?": the user picks an author (or makes a card) and says they are sure</summary>
    private Task AssignAuthorAsync(LiveryEntry entry, Window? owner = null) => AssignAuthorAsync([entry], owner);

    /// <summary>"Whose liveries are these?": one author for all of them</summary>
    /// <param name="entries">The liveries</param>
    /// <param name="owner">The window the dialog opens over; the gallery when null</param>
    /// <param name="skipped">Selected liveries left out because their id is an account's</param>
    private async Task AssignAuthorAsync(IReadOnlyList<LiveryEntry> entries, Window? owner = null, int skipped = 0)
    {
        owner ??= this;
        var names = _mainViewModel.AuthorNames();
        // Every livery's hints, the first of each author kept: the dialog shows one row per author
        var hints = entries.SelectMany(e => e.AuthorHints).ToList();
        var dialog = new AssignAuthorDialog(entries, _mainViewModel.AuthorChoices(), hints, names.CardOf,
            over => EditAuthorCardAsync(over, null), skipped);
        if (!await dialog.ShowDialog<bool>(owner) || dialog.Result is not { } card) return;
        if (!await _mainViewModel.AssignAuthorAsync(entries, card))
            await InfoDialog.ShowAsync(owner, Strings.AssignAuthorDialogTitle, Strings.AssignAuthorFailedMessage);
    }

    /// <summary>One livery: its tags as text. Several: every tag of the gallery, ticked on all, some or none of them</summary>
    private async Task ShowEditTagsAsync(IReadOnlyList<LiveryEntry> entries)
    {
        if (entries.Count == 1)
        {
            var entry = entries[0];
            var single = new TagEditDialog(entry.Tags);
            if (await single.ShowDialog<bool>(this)) _mainViewModel.ApplyTagsEdit(entry, single.ResultTags);
            return;
        }

        var knownTags = _mainViewModel.TagsBar.AllTags.Select(t => t.Tag).ToList();
        var dialog = new TagEditDialog(entries.Select(e => e.Tags).ToList(), knownTags);
        if (await dialog.ShowDialog<bool>(this))
            _mainViewModel.ApplyTagsChange(entries, dialog.TagsToAdd, dialog.TagsToRemove);
    }

    /// <summary>A right click on a tag in the tags row hides its liveries (or shows them again)</summary>
    private void TagChip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (sender is not StyledElement { DataContext: TagChipViewModel chip }) return;
        chip.ToggleHideCommand.Execute(null);
        e.Handled = true;
    }

    private async Task ShowPreviewAsync(LiveryEntry entry)
    {
        string? savePath = _savePathService.ResolveSaveDataPath();
        string? webpPath = savePath is not null ? _profile.FindPreview(savePath, entry.FolderName) : null;
        if (webpPath is null && entry.Data.ExternalPreviewPath is { } externalPreview && File.Exists(externalPreview))
            webpPath = externalPreview;
        // No preview in the save: the one the gallery drew from the 3D model
        string? drawn = entry.Data.ThumbnailDrawn ? entry.ThumbnailPath : entry.ThumbnailPath is null ? entry.RenderedThumbnailPath : null;
        if (webpPath is null && drawn is not null && File.Exists(drawn))
            webpPath = drawn;

        if (webpPath is null)
        {
            await InfoDialog.ShowAsync(this, Strings.PreviewDialogTitle, Strings.PreviewNotFoundMessage);
            return;
        }

        var dialog = new LiveryPreviewDialog(webpPath);
        await dialog.ShowDialog(this);
    }

    private void Card_SelectPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed) return;
        if (sender is not StyledElement element || element.DataContext is not LiveryEntry entry) return;

        _mainViewModel.Gallery.ToggleSelectionCommand.Execute(entry);
    }

    private void Card_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not StyledElement element || element.DataContext is not LiveryEntry entry) return;
        if (!entry.IsSelected) _mainViewModel.Gallery.SelectOnlyCommand.Execute(entry);
    }

    private void ContactsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(() => new ContactsDialog().ShowDialog(this), "contacts dialog");
    }

    private void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(() => new AboutDialog(_aboutComponents, _profile).ShowDialog(this), "about dialog");
    }

    private void OpenSettingsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(ShowSettingsAsync, "settings dialog");
    }

    private async Task ShowSettingsAsync()
    {
        var dlg = new SettingsDialog(_settings, _savePathService.SavePath, _gameFolderCheck, _profile, ExportDiagnosticsAsync,
            new AccountDataActions(_mainViewModel.ListStoredAccountsAsync, _mainViewModel.ForgetAccountAsync),
            _mainViewModel.CarTextLanguages);
        await dlg.ShowDialog(this);
        if (_isClosing) return;
        if (dlg.SavePathChanged) await _mainViewModel.HandleSavePathChangedFromSettingsAsync();
        SyncSaveFolderWatcher();
        ApplyServerConnectionSetting();
        _mainViewModel.RefreshDisplaySettings();
        await ApplyGamePathAsync();
        _mainViewModel.StartCarTextsUpdate(); // the game folder may have changed
    }

    private Task ExportDiagnosticsAsync(Stream destination, CancellationToken ct)
    {
        // CurrentUserId logs an error when there is no save folder yet, so it is only asked when there is one
        ulong? userId = _savePathService.SavePath is null ? null : _savePathService.CurrentUserId;
        string? accountKey = _savePathService.SavePath is { } savePath ? AccountKey.For(savePath) : null;
        var sources = new DiagnosticsSources(_settings, _savePathService.SavePath, _viewer3D.GameDirectory, userId, accountKey);
        return _diagnosticsService.WriteZipAsync(destination, sources, ct);
    }

    private void AuthorsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AuthorsDialog(_authorCardService, _mainViewModel.Gallery.AllEntries.ToList(), async () =>
        {
            await _mainViewModel.RefreshAfterAuthorsChangedAsync();
            return _mainViewModel.Gallery.AllEntries.ToList();
        });
        RunUiTask(() => dialog.ShowDialog(this), "authors dialog");
    }

    private void OpenArchiveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dialog = new ArchiveDialog(_archiveService, _savePathService,
            () => [.. _mainViewModel.Gallery.AllEntries.Select(entry => entry.Data)],
            () => _mainViewModel.RunScanAsyncTracked(isUserInitiated: true),
            _mainViewModel.GetAccountContext(), _mainViewModel.ShowFolderNames);
        RunUiTask(() => dialog.ShowDialog(this), "archive dialog");
    }

    private void OpenBackupsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dialog = new BackupsDialog(
            _backupService, _savePathService,
            () => [.. _mainViewModel.Gallery.AllEntries.Select(entry => entry.Data)],
            () => _mainViewModel.RunScanAsyncTracked(isUserInitiated: true),
            _mainViewModel.GetAccountContext());
        RunUiTask(() => dialog.ShowDialog(this), "backups dialog");
    }

    private StatisticsWindow? _statisticsWindow;

    /// <summary>One statistics window beside the gallery; the button brings it forward when it is open</summary>
    private void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_statisticsWindow is { } open)
        {
            open.Activate();
            return;
        }
        StatisticsWindow? window = null;
        var viewModel = new StatisticsViewModel(_mainViewModel.StatisticsHost(
            entry => RunUiTask(() => ShowAuthorRowAsync(entry, window), "author card from statistics"),
            () => [.. _backupService.GetAll().Select(b => b.Manifest)]));
        window = new StatisticsWindow(viewModel);
        void Refresh(GalleryCountsSnapshot _) => window.RequestRefresh();
        void RefreshOnTags() => window.RequestRefresh();
        _mainViewModel.Gallery.CountsUpdated += Refresh;
        _mainViewModel.Gallery.SelectedTagsChanged += RefreshOnTags;
        window.Closed += (_, _) =>
        {
            _mainViewModel.Gallery.CountsUpdated -= Refresh;
            _mainViewModel.Gallery.SelectedTagsChanged -= RefreshOnTags;
            _statisticsWindow = null;
        };
        _statisticsWindow = window;
        window.Show(this);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
