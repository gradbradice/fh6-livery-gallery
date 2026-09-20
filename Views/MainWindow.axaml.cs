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
    private readonly CarDatabaseService _carDb;
    private readonly TagService _tagService;
    private readonly AuthorCardService _authorCardService;
    private MainViewModel _mainViewModel = null!;
    private SavePathService _savePathService = null!;
    private string? SaveDataPath => _savePathService.ResolveSaveDataPath();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AppSettingsData _settings;
    private readonly DispatcherTimer _autoScanTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _isLoaded;
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _resizeDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(
        AppSettingsData settings,
        CarDatabaseService carDatabase,
        TagService tagService,
        FavoriteService favoriteService,
        AuthorCardService authorCardService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService)
    {
        InitializeComponent();

        _settings = settings;
        _carDb = carDatabase;
        _tagService = tagService;
        _authorCardService = authorCardService;
        _savePathService = new SavePathService(this, settings);

        _mainViewModel = new MainViewModel(
            settings, _savePathService, carDatabase, favoriteService, scanService, updateService)
        {
            ShutdownToken = _shutdownCts.Token
        };

        DataContext = _mainViewModel;
        _mainViewModel.AuthorRowRequested += async entry => await ShowAuthorRowAsync(entry);
        _mainViewModel.EditTagsRequested += async entry => await ShowEditTagsAsync(entry);

        GroupsHost.ItemsSource = _mainViewModel.Gallery.DisplayedGroups;
        _mainViewModel.Gallery.GroupsReplaced += () =>
        {
            GroupsHost.InvalidateMeasure();
            GalleryScroll.InvalidateMeasure();
        };
        TagsFilterRow.DataContext = _mainViewModel.TagsBar;
        SearchBox.DataContext = _mainViewModel.FilterBar;
        DisplayFilterButton.DataContext = _mainViewModel.FilterBar;
        StatusBar.DataContext = _mainViewModel.Status;
        GalleryHost.DataContext = _mainViewModel.Status;
        RefreshButton.DataContext = _mainViewModel;
        UpdateBanner.DataContext = _mainViewModel.Update;

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

        _autoScanTimer.Tick += (_, __) => _ = _mainViewModel.RunScanAsyncTracked(isUserInitiated: false);
        SizeChanged += (_, __) =>
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        };

        Loaded += MainWindow_Loaded;
        _autoScanTimer.Start();
        Closing += MainWindow_Closing;
    }

    private bool _isClosing;

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosing) return;

        e.Cancel = true;
        _isClosing = true;

        _autoScanTimer.Stop();
        _searchDebounceTimer.Stop();
        _resizeDebounceTimer.Stop();
        _mainViewModel.ScanController.Cancel();
        _shutdownCts.Cancel();

        try { await _mainViewModel.ScanController.WaitAsync(); }
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
        AppLogger.Shutdown();

        Close();
    }

    private void UpdateGroupWidthsOnly() => _mainViewModel.Gallery.GroupWidth = ComputeGroupWidth();

    private static void FireAndForget(Func<Task> action, string context) => _ = RunSafelyAsync(action, context);

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

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateGroupWidthsOnly();
        FireAndForget(CheckForUpdatesAsync, nameof(CheckForUpdatesAsync));

        _carDb.LoadLocal();

        bool savedPathMissing = _savePathService.WasSavedPathMissing;
        _savePathService.InitializeFromSettings();

        if (savedPathMissing)
        {
            await InfoDialog.ShowAsync(this, Strings.FolderNotFoundTitle, Strings.SavedPathNotFoundNotice);
        }

        if (_savePathService.SavePath is null)
        {
            await _mainViewModel.PromptForSavePathAsync(initial: true);
            return;
        }

        await _mainViewModel.RunScanAsyncTracked(isUserInitiated: true);
        FireAndForget(() => _mainViewModel.RefreshCarDatabaseAsync(showLoadingOverlay: false, _shutdownCts.Token), nameof(MainViewModel.RefreshCarDatabaseAsync));
    }

    private async Task CheckForUpdatesAsync() => await _mainViewModel.Update.CheckAsync(_shutdownCts.Token);

    private async void UpdateBanner_Click(object? sender, RoutedEventArgs e) => await _mainViewModel.Update.ShowDetailsAsync(this);

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
        var card = entry.AuthorIdentityTagHex is not null
            ? _authorCardService.FindCardForIdentityTag(entry.AuthorIdentityTagHex)
            : null;
        if (card is null)
        {
            await InfoDialog.ShowAsync(this, Strings.AuthorCardViewNoCardTitle,
                string.Format(Strings.AuthorCardViewNoCardMessage, entry.AuthorRaw));
            return;
        }

        var dialog = new AuthorCardViewDialog(card, _mainViewModel.Gallery.AllEntries.ToList());
        await dialog.ShowDialog(this);
    }

    private async Task ShowEditTagsAsync(LiveryEntry entry)
    {
        var dialog = new TagEditDialog(entry.Tags);
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            entry.Tags = dialog.ResultTags;
            _tagService.SetTags(entry.FolderName, entry.Tags);
            _mainViewModel.RebuildTagsBar();

            if (_mainViewModel.Gallery.SelectedTags.Count > 0)
                RefreshGallery();
        }
    }

    private void Card_SelectPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed) return;
        if (sender is not StyledElement element || element.DataContext is not LiveryEntry entry) return;

        _mainViewModel.Gallery.ToggleSelectionCommand.Execute(entry);
    }

    private async void Card_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) await ThumbnailLifecycleController.OnAttachedAsync(control);
    }

    private void Card_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) ThumbnailLifecycleController.OnDetached(control);
    }

    private async void Card_DataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Control control) await ThumbnailLifecycleController.OnDataContextChangedAsync(control);
    }

    private async void ContactsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dlg = new ContactsDialog();
        await dlg.ShowDialog(this);
    }

    private async void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dlg = new AboutDialog();
        await dlg.ShowDialog(this);
    }

    private async void OpenSettingsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dlg = new SettingsDialog(_settings, _savePathService.SavePath);
        await dlg.ShowDialog(this);
        if (dlg.SavePathChanged)
        {
            _savePathService.SyncFromSettings();
            if (SaveDataPath is not null) await _mainViewModel.RunScanAsyncTracked(isUserInitiated: true);
        }
        if (dlg.LanguageChanged) OnLanguageChanged();
    }

    private async void AuthorsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AuthorsDialog(_authorCardService, _mainViewModel.Gallery.AllEntries.ToList(), async () =>
        {
            await _mainViewModel.RefreshEntriesFromCacheAsync();
            return _mainViewModel.Gallery.AllEntries.ToList();
        });
        await dialog.ShowDialog(this);
    }

    private async void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        var stats = GalleryStatisticsService.CalculateOverall(_mainViewModel.Gallery.AllEntries.ToList());
        string message = GalleryStatisticsService.FormatOverallMessage(stats);
        await InfoDialog.ShowAsync(this, Strings.StatsTitle, message);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}