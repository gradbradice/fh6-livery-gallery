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
    private MainViewModel _mainViewModel = null!;
    private SavePathService _savePathService = null!;
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
        LiveryArchiveService archiveService,
        LiveryBackupService backupService,
        LiveryScanner scanService,
        AppUpdateCheckService updateService)
    {
        InitializeComponent();

        _settings = settings;
        _authorCardService = authorCardService;
        _archiveService = archiveService;
        _backupService = backupService;
        _savePathService = new SavePathService(settings);
        var savePathPrompter = new SavePathPrompter(this);

        _mainViewModel = new MainViewModel(
            settings, savePathPrompter, _savePathService, carDatabase, tagService, authorCardService,
            favoriteService, archiveService, scanService, updateService)
        {
            ShutdownToken = _shutdownCts.Token
        };

        DataContext = _mainViewModel;
        _mainViewModel.Gallery.AuthorRowRequested += async entry => await ShowAuthorRowAsync(entry);
        _mainViewModel.Gallery.EditTagsRequested += async entry => await ShowEditTagsAsync(entry);
        _mainViewModel.Gallery.ViewPreviewRequested += async entry => await ShowPreviewAsync(entry);
        _mainViewModel.ErrorMessageRequested += async message =>
            await InfoDialog.ShowAsync(this, Strings.AppTitle, message);

        GroupsHost.ItemsSource = _mainViewModel.Gallery.DisplayedGroups;
        _mainViewModel.Gallery.GroupsReplaced += () =>
        {
            GroupsHost.InvalidateMeasure();
            GalleryScroll.InvalidateMeasure();
        };

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
        AppLocalisationService.LanguageChanged += OnLanguageChanged;
    }

    private bool _isClosing;
    private Task? _initializeTask;

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosing) return;

        e.Cancel = true;
        _isClosing = true;
        _autoScanTimer.Stop();
        _searchDebounceTimer.Stop();
        _resizeDebounceTimer.Stop();
        _shutdownCts.Cancel();

        if (_initializeTask is not null)
        {
            try
            {
                await _initializeTask;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("InitializeAsync faulted before shutdown could wait for it", ex);
            }
        }

        await _mainViewModel.ShutdownAsync();

        Close();
    }

    private void UpdateGroupWidthsOnly() => _mainViewModel.Gallery.GroupWidth = ComputeGroupWidth();

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateGroupWidthsOnly();
        if (_mainViewModel.WasSavedPathMissing)
            await InfoDialog.ShowAsync(this, Strings.FolderNotFoundTitle, Strings.SavedPathNotFoundNotice);

        _initializeTask = _mainViewModel.InitializeAsync(_shutdownCts.Token);
        await _initializeTask;
    }

    private async void UpdateBanner_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainViewModel.Update.LatestVersion is null) return;
        var dialog = new WhatsNewDialog(
            _mainViewModel.Update.LatestVersion, _mainViewModel.Update.ReleaseBody, _mainViewModel.Update.ReleaseUrl);
        await dialog.ShowDialog(this);
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
        if (dlg.SavePathChanged) await _mainViewModel.HandleSavePathChangedFromSettingsAsync();
        _mainViewModel.RefreshDisplaySettings();
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

    private async void OpenArchiveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dialog = new ArchiveDialog(_archiveService, _savePathService,
            () => _mainViewModel.RunScanAsyncTracked(isUserInitiated: true));
        await dialog.ShowDialog(this);
    }

    private async void OpenBackupsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dialog = new BackupsDialog(
            _backupService, _savePathService,
            () => [.. _mainViewModel.Gallery.AllEntries.Select(entry => entry.Data)],
            () => _mainViewModel.RunScanAsyncTracked(isUserInitiated: true));
        await dialog.ShowDialog(this);
    }

    private async void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        string message = _mainViewModel.GetOverallStatsMessage();
        await InfoDialog.ShowAsync(this, Strings.StatsTitle, message);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.HandleTitleBarDragOrMaximize(e, MaximizeIcon);

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => this.ToggleMaximize(MaximizeIcon);

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}