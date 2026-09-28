using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveryGallery.Controller;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class MainWindow : Window
{
    private readonly AuthorCardService _authorCardService;
    private readonly LiveryArchiveService _archiveService;
    private readonly LiveryBackupService _backupService;
    private readonly LiveryRenderService _renderService;
    private LiveryViewerWindow? _viewerWindow;
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
    private static readonly TimeSpan ViewerLoadDrainTimeout = TimeSpan.FromSeconds(30);

    public MainWindow(
        AppSettingsData settings,
        CarDatabaseService carDatabase,
        TagService tagService,
        FavoriteService favoriteService,
        AuthorCardService authorCardService,
        LiveryArchiveService archiveService,
        LiveryBackupService backupService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService,
        LiveryRenderService renderService,
        SaveFolderLock saveFolderLock)
    {
        InitializeComponent();

        _saveFolderLock = saveFolderLock;

        _settings = settings;
        _authorCardService = authorCardService;
        _archiveService = archiveService;
        _backupService = backupService;
        _renderService = renderService;
        _savePathService = new SavePathService(settings);
        var savePathPrompter = new SavePathPrompter(this);

        _mainViewModel = new MainViewModel(
            settings, savePathPrompter, _savePathService, carDatabase, tagService, authorCardService,
            favoriteService, archiveService, scanService, updateService)
        {
            ShutdownToken = _shutdownCts.Token
        };

        DataContext = _mainViewModel;
        _saveFolderWatcher = new SaveFolderWatcher(ct => _mainViewModel.RunAutoRefreshAsync(ct));
        _mainViewModel.SavePathChanged += SyncSaveFolderWatcher;

        _serverConnection = new ServerConnectionController(
            new LiveryServerService(),
            new InstalledLiveriesCache(),
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
        Activated += (_, __) => _serverConnection.OnWindowActivated();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                _serverConnection.SetWindowVisible(WindowState != WindowState.Minimized);
        };
        _mainViewModel.Gallery.AuthorRowRequested += entry => RunUiTask(() => ShowAuthorRowAsync(entry), "author card");
        _mainViewModel.Gallery.EditTagsRequested += entry => RunUiTask(() => ShowEditTagsAsync(entry), "edit tags");
        _mainViewModel.Gallery.ViewPreviewRequested += entry => RunUiTask(() => ShowPreviewAsync(entry), "preview");
        _mainViewModel.Gallery.View3DRequested += entry =>
            RunUiTask(() => ShowLivery3DAsync(entry), $"3D viewer for '{entry.FolderName}'");
        _renderService.StateChanged += OnRenderServiceStateChanged;
        _mainViewModel.ErrorMessageRequested += message =>
            RunUiTask(() => InfoDialog.ShowAsync(this, Strings.AppTitle, message), "error message");
        _mainViewModel.ConfirmArchiveWithoutInstalledDataAsync = count => AskArchiveWarningAsync(
            count == 1 ? Strings.ArchiveInstalledUnknownMessage : Strings.ArchiveInstalledUnknownMessageMany);
        _mainViewModel.ConfirmArchiveWithOutdatedInstalledDataAsync = count => AskArchiveWarningAsync(
            count == 1 ? Strings.ArchiveInstalledOutdatedMessage : Strings.ArchiveInstalledOutdatedMessageMany);
        _mainViewModel.IsInstalledDataOutdatedAsync = () => _serverConnection.IsInstalledResultOutdatedAsync(_shutdownCts.Token);

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
            AppLogger.LogError("Shutdown failed; closing anyway", ex);
        }
        finally
        {
            AppLogger.Shutdown();
            _shutdownCompleted = true;
            Close();
        }
    }

    private async Task ShutdownAsync()
    {
        _isClosing = true;
        _shutdownCts.Cancel();
        _renderService.StateChanged -= OnRenderServiceStateChanged;
        AppLocalisationService.LanguageChanged -= OnLanguageChanged;
        _searchDebounceTimer.Stop();
        _resizeDebounceTimer.Stop();
        ThumbnailLifecycleController.BeginShutdown();
        _viewerWindow?.Close();

        await AwaitShutdownStepAsync(_saveFolderWatcher.StopAsync(), "save folder watcher", ShutdownStepTimeout);
        await AwaitShutdownStepAsync(_serverConnection.StopAsync(), "livery server connection", ShutdownStepTimeout);
        await AwaitShutdownStepAsync(_initializeTask, "initialization", ShutdownStepTimeout);
        await _uiTasks.WaitAllAsync(ShutdownStepTimeout);
        await AwaitShutdownStepAsync(_saveFolderLock.CloseAsync(), "save folder operations", Timeout.InfiniteTimeSpan);
        await AwaitShutdownStepAsync(_mainViewModel.ShutdownAsync(), "gallery", Timeout.InfiniteTimeSpan);
        await AwaitShutdownStepAsync(_renderService.ShutdownAsync(ShutdownStepTimeout), "3D renderer", Timeout.InfiniteTimeSpan);
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
            AppLogger.LogWarning($"Shutdown: '{name}' did not finish within {timeout.TotalSeconds:0.#} s, continuing without it");
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the shutdown itself
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Shutdown: '{name}' failed", ex);
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
        _initializeTask = _mainViewModel.InitializeAsync(_shutdownCts.Token);
        await _initializeTask;
        if (_isClosing) return;
        SyncSaveFolderWatcher();
        ApplyServerConnectionSetting();
    }

    private void ApplyServerConnectionSetting()
    {
        if (_isClosing) return;
        _mainViewModel.FilterBar.SetInstalledFilterAvailable(_settings.ServerConnectionEnabled);
        _serverConnection.Configure(_settings.ServerConnectionEnabled);
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
                ? await GameDiscoveryService.TryFindGamePathAsync()
                : _settings.GameInstallPath;
            if (_isClosing) return;
            await _renderService.SetGamePathAsync(path);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to apply the game folder", ex);
        }
    }

    private void OnRenderServiceStateChanged()
    {
        LiveryEntry.View3DGameFolderProblem = _renderService switch
        {
            { IsChecking: true } => Strings.View3DGameFolderChecking,
            { IsAvailable: true } => null,
            _ => string.Format(Strings.View3DGameFolderInvalidFormat,
                GameFolderText.FirstError(_renderService.FolderCheck) ?? Strings.GameFolderProblemUnknown)
        };
        _mainViewModel.Gallery.RefreshView3DAvailability();

        if (_viewerWindow is not null && !_renderService.IsChecking
            && !ReferenceEquals(_viewerWindow.Renderer, _renderService.CurrentRenderer))
            _viewerWindow.Close();
    }

    private async Task ShowLivery3DAsync(LiveryEntry entry)
    {
        if (!_renderService.IsAvailable)
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

        ForzaToolkit.Formats.ParseResult<ForzaToolkit.LiveryRender.Livery.LiveryDrawing> drawing;
        string cLiveryPath = Path.Combine(savePath, entry.FolderName, "C_livery");
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(cLiveryPath);
            drawing = await Task.Run(() => LiveryMapping.ToDrawing((uint)Math.Max(0, entry.CarId), bytes));
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"3D viewer: failed to read '{cLiveryPath}'", ex);
            await InfoDialog.ShowAsync(this, Strings.View3DDialogTitle, Strings.View3DReadFailed);
            return;
        }

        if (drawing.Error is { } parseError)
        {
            AppLogger.LogError($"3D viewer: C_livery of '{entry.FolderName}' parsed with status {drawing.Status}: {parseError.Message}",
                new InvalidDataException(parseError.Message));
        }
        if (!drawing.HasValue)
        {
            string code = drawing.Error is { } e ? $"{e.Code} ({(int)e.Code})" : "?";
            await InfoDialog.ShowAsync(this, Strings.View3DDialogTitle, string.Format(Strings.View3DParseFailedFormat, code));
            return;
        }

        var acquired = _viewerWindow is null ? await _renderService.AcquireAsync() : null;
        if (_isClosing)
        {
            if (acquired is not null) _renderService.Release();
            return;
        }
        if (_viewerWindow is null)
        {
            if (acquired is null)
            {
                await InfoDialog.ShowAsync(this, Strings.View3DDialogTitle, entry.View3DTooltip);
                return;
            }

            var window = new LiveryViewerWindow(acquired, _settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_viewerWindow, window)) _viewerWindow = null;
                RunUiTask(() => ReleaseRendererAfterLoadsAsync(window), "release the 3D renderer");
            };
            _viewerWindow = window;
            window.Show(this);
        }
        else
        {
            if (acquired is not null) _renderService.Release();
            _viewerWindow.Activate();
        }

        await _viewerWindow.ShowLiveryAsync(entry.LiveryName, drawing.Value!);
    }

    private Task<(bool Confirmed, bool DontShowAgain)> AskArchiveWarningAsync(string message) =>
        ConfirmDialog.AskWithOptionAsync(
            this, Strings.ContextMenuMoveToArchive, message,
            Strings.DontShowAgainCheckBox, Strings.ButtonYes, Strings.ButtonCancel);

    private async Task ReleaseRendererAfterLoadsAsync(LiveryViewerWindow window)
    {
        try
        {
            if (!await window.WaitForLoadsAsync(ViewerLoadDrainTimeout))
                AppLogger.LogWarning($"3D viewer: a load was still running {ViewerLoadDrainTimeout.TotalSeconds:0} s after the window closed; releasing the renderer anyway");
        }
        finally
        {
            _renderService.Release();
        }
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

    private async Task ShowAuthorRowAsync(LiveryEntry entry)
    {
        var card = _mainViewModel.FindAuthorCard(entry);
        if (card is null && entry.Data.IsAuthorUnknown)
        {
            await InfoDialog.ShowAsync(this, Strings.AuthorCardViewNoCardTitle, Strings.AuthorUnknownMessage);
            return;
        }
        if (card is null)
        {
            bool wantsToCreate = await InfoDialog.ShowWithActionAsync(this, Strings.AuthorCardViewNoCardTitle,
                string.Format(Strings.AuthorCardViewNoCardMessage, entry.AuthorRaw), Strings.AuthorCardCreateButton);
            if (wantsToCreate) await CreateAuthorCardAsync(entry);
            return;
        }

        var dialog = new AuthorCardViewDialog(card, _mainViewModel.Gallery.AllEntries.ToList());
        await dialog.ShowDialog(this);
    }

    private async Task CreateAuthorCardAsync(LiveryEntry entry)
    {
        var (availableAliases, nameToTags) = _mainViewModel.BuildAuthorAliasIndex();

        var dialog = new AuthorCardEditDialog(
            _authorCardService, availableAliases, nameToTags, existingCard: null, preselectedAlias: entry.AuthorRaw);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved || dialog.Result is null) return;

        await _mainViewModel.TrySaveAuthorCard(dialog.Result);
    }

    private async Task ShowEditTagsAsync(LiveryEntry entry)
    {
        var dialog = new TagEditDialog(entry.Tags);
        var result = await dialog.ShowDialog<bool>(this);
        if (result) _mainViewModel.ApplyTagsEdit(entry, dialog.ResultTags);
    }

    private async Task ShowPreviewAsync(LiveryEntry entry)
    {
        string? savePath = _savePathService.ResolveSaveDataPath();
        string? webpPath = savePath is not null
            ? ThumbnailService.FindSourceThumbnail(Path.Combine(savePath, entry.FolderName))
            : null;
        if (webpPath is null && entry.Data.ExternalPreviewPath is { } externalPreview && File.Exists(externalPreview))
            webpPath = externalPreview;

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

    private async void Card_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnAttachedAsync(control);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in Card_AttachedToVisualTree", ex);
        }
    }

    private void Card_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) ThumbnailLifecycleController.OnDetached(control);
    }

    private async void Card_DataContextChanged(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Control control) await ThumbnailLifecycleController.OnDataContextChangedAsync(control);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in Card_DataContextChanged", ex);
        }
    }

    private void ContactsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(() => new ContactsDialog().ShowDialog(this), "contacts dialog");
    }

    private void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(() => new AboutDialog().ShowDialog(this), "about dialog");
    }

    private void OpenSettingsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        RunUiTask(ShowSettingsAsync, "settings dialog");
    }

    private async Task ShowSettingsAsync()
    {
        var dlg = new SettingsDialog(_settings, _savePathService.SavePath);
        await dlg.ShowDialog(this);
        if (_isClosing) return;
        if (dlg.SavePathChanged) await _mainViewModel.HandleSavePathChangedFromSettingsAsync();
        SyncSaveFolderWatcher();
        ApplyServerConnectionSetting();
        _mainViewModel.RefreshDisplaySettings();
        await ApplyGamePathAsync();
    }

    private void AuthorsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AuthorsDialog(_authorCardService, _mainViewModel.Gallery.AllEntries.ToList(), async () =>
        {
            await _mainViewModel.RefreshEntriesFromCacheAsync();
            return _mainViewModel.Gallery.AllEntries.ToList();
        });
        RunUiTask(() => dialog.ShowDialog(this), "authors dialog");
    }

    private void OpenArchiveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dialog = new ArchiveDialog(_archiveService, _savePathService,
            () => [.. _mainViewModel.Gallery.AllEntries.Select(entry => entry.Data)],
            () => _mainViewModel.RunScanAsyncTracked(isUserInitiated: true));
        RunUiTask(() => dialog.ShowDialog(this), "archive dialog");
    }

    private void OpenBackupsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dialog = new BackupsDialog(
            _backupService, _savePathService,
            () => [.. _mainViewModel.Gallery.AllEntries.Select(entry => entry.Data)],
            () => _mainViewModel.RunScanAsyncTracked(isUserInitiated: true));
        RunUiTask(() => dialog.ShowDialog(this), "backups dialog");
    }

    private void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        string message = _mainViewModel.GetOverallStatsMessage();
        RunUiTask(() => InfoDialog.ShowAsync(this, Strings.StatsTitle, message), "statistics");
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}