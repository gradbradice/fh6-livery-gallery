using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.Views;

internal partial class MainWindow : Window
{
    private readonly AppCacheService _cacheService;
    private readonly CarDatabaseService _carDb;
    private readonly TagService _tagService;
    private readonly FavoriteService _favoriteService;
    private readonly LiveryScanService _scanService;
    private readonly AppUpdateCheckService _updateService;

    private List<LiveryEntry> _allEntries = [];
    private readonly ObservableCollection<LiveryGroup> _displayedGroups = [];
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private string? _savePath;
    private bool _savePathLostNotified;

    private string? SaveDataPath
    {
        get
        {
            if (_savePath is null) return null;
            int? hint = _settings.LastKnownContainerSavePath == _savePath
                ? _settings.LastKnownContainerId
                : null;

            var (path, containerId) = LocalSaveService.GetSaveDataPathWithId(_savePath, hint);
            if (containerId is not null
                && (containerId != _settings.LastKnownContainerId || _settings.LastKnownContainerSavePath != _savePath))
            {
                _settings.LastKnownContainerId = containerId;
                _settings.LastKnownContainerSavePath = _savePath;
                AppSettingsService.Save(_settings);
            }
            return path;
        }
    }

    private CancellationTokenSource? _scanCts;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AppSettingsData _settings;
    private bool _isScanning;
    private readonly DispatcherTimer _autoScanTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private LiveryScanEntry? _lastScanResult;
    private string? _updateReleaseUrl;
    private string? _latestVersion;
    private string? _updateReleaseBody;

    private bool _isLoaded;
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _resizeDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(
        AppSettingsData settings,
        AppCacheService cacheService,
        CarDatabaseService carDatabase,
        TagService tagService,
        FavoriteService favoriteService,
        LiveryScanService scanService,
        AppUpdateCheckService updateService)
    {
        InitializeComponent();
        GroupsHost.ItemsSource = _displayedGroups;
        _settings = settings;
        _cacheService = cacheService;
        _carDb = carDatabase;
        _tagService = tagService;
        _favoriteService = favoriteService;
        _scanService = scanService;
        _updateService = updateService;

        UpdateDisplayFilterChecks();
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

        _autoScanTimer.Tick += (_, __) =>
        {
            _autoScanTimer.Stop();
            _ = RunScanAsync(isUserInitiated: false);
        };
        SizeChanged += (_, __) =>
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        };

        Loaded += MainWindow_Loaded;
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
        _scanCts?.Cancel();
        _shutdownCts.Cancel();
        await Task.Run(() =>
        {
            _favoriteService.Flush();
            _tagService.Flush();
            _cacheService.Flush();
            AppSettingsService.Flush();
            AppLogger.Shutdown();
        });

        Close();
    }

    private void UpdateGroupWidthsOnly()
    {
        if (GroupsHost.ItemsSource is not IEnumerable<LiveryGroup> groups) return;
        double groupWidth = ComputeGroupWidth();
        foreach (var group in groups)
            group.GroupWidth = groupWidth;
    }

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
        FireAndForget(CheckForUpdatesAsync, nameof(CheckForUpdatesAsync));

        _carDb.LoadLocal();

        bool savedPathMissing = !string.IsNullOrWhiteSpace(_settings.SavePath)
            && !Directory.Exists(_settings.SavePath);

        _savePath = !string.IsNullOrWhiteSpace(_settings.SavePath) && Directory.Exists(_settings.SavePath)
            ? _settings.SavePath
            : LocalSaveService.FindLocalSavePath();

        if (savedPathMissing)
        {
            await InfoDialog.ShowAsync(this, Strings.FolderNotFoundTitle, Strings.SavedPathNotFoundNotice);
        }

        if (_savePath is null)
        {
            await PromptForSavePathAsync(initial: true);
            return;
        }

        await RunScanAsync(isUserInitiated: true);
        FireAndForget(() => RefreshCarDatabaseAsync(showLoadingOverlay: false), nameof(RefreshCarDatabaseAsync));
    }

    private async Task CheckForUpdatesAsync()
    {
        var result = await _updateService.CheckAsync(_shutdownCts.Token);
        if (!result.IsNewer || result.LatestVersion is null) return;

        _latestVersion = result.LatestVersion;
        _updateReleaseUrl = result.ReleaseUrl;
        _updateReleaseBody = result.ReleaseBody;
        UpdateBannerText.Text = string.Format(Strings.UpdateAvailableFormat, _latestVersion);
        UpdateBanner.IsVisible = true;
    }

    private async void UpdateBanner_Click(object? sender, RoutedEventArgs e)
    {
        if (_latestVersion is null) return;
        var dlg = new WhatsNewDialog(_latestVersion, _updateReleaseBody, _updateReleaseUrl);
        await dlg.ShowDialog(this);
    }

    private async Task RefreshCarDatabaseAsync(bool showLoadingOverlay = true)
    {
        if (showLoadingOverlay) SetLoading(true, Strings.CarDbUpdating);
        try
        {
            var outcome = await _carDb.RefreshAsync(_shutdownCts.Token);
            string countText = $"{_carDb.Count} {Strings.CarDbCarsWord}";
            if (_carDb.LastError is not null)
            {
                StatusText.Text = _carDb.HasLocalData
                    ? string.Format(Strings.CarDbDownloadFailed, countText)
                    : Strings.CarDbNoDataAtAll;
            }
            else
            {
                StatusText.Text = outcome == CarDatabaseRefreshOutcome.Updated
                    ? string.Format(Strings.CarDbUpdated, countText)
                    : string.Format(Strings.CarDbUpToDate, countText);
            }

            if (outcome == CarDatabaseRefreshOutcome.Updated && !_isScanning && SaveDataPath is not null)
            {
                var freshEntries = await _scanService.RegenerateEntriesAsync(_shutdownCts.Token);
                _allEntries = MergeWithLocalState(freshEntries);
                RebuildTagsBar();
                RefreshGallery();
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        finally
        {
            if (showLoadingOverlay) SetLoading(false);
        }
    }

    private async Task PromptForSavePathAsync(bool initial)
    {
        if (initial)
        {
            bool yes = await ConfirmDialog.AskAsync(
                this,
                Strings.FolderNotFoundTitle,
                Strings.FolderNotFoundMessage);

            if (!yes)
            {
                StatusText.Text = Strings.SavePathNotChosen;
                EmptyStateText.Text = Strings.SavePathNotChosen;
                EmptyState.IsVisible = true;
                return;
            }
        }

        await BrowseForFolderAsync();
    }

    private async Task BrowseForFolderAsync()
    {
        var provider = StorageProvider;
        if (provider is null) return;

        while (true)
        {
            var result = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Strings.SelectFolderDialogTitle,
                AllowMultiple = false
            });

            var folder = result.Count > 0 ? result[0] : null;
            string? path = folder?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            if (!LocalSaveService.IsSavePathValid(path))
            {
                bool retry = await ConfirmDialog.AskAsync(
                    this,
                    Strings.SaveFolderValidationFailedTitle,
                    Strings.SaveFolderValidationFailed,
                    yesText: Strings.ButtonRetry,
                    noText: Strings.ButtonCancel);

                if (!retry) return;
                continue;
            }

            _savePath = path;
            _settings.SavePath = _savePath;
            AppSettingsService.Save(_settings);
            await RunScanAsync(isUserInitiated: true);
            return;
        }
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await Task.WhenAll(RefreshCarDatabaseAsync(), CheckForUpdatesAsync());

        if (!_settings.RefreshLiveriesOnButtonClick) return;

        if (_savePath is null)
        {
            await PromptForSavePathAsync(initial: true);
            return;
        }
        await RunScanAsync(isUserInitiated: true);
    }

    private async Task RunScanAsync(bool isUserInitiated)
    {
        string? saveDataPath = SaveDataPath;
        if (saveDataPath is null)
        {
            if (!_savePathLostNotified)
            {
                _savePathLostNotified = true;
                await PromptForSavePathAsync(initial: true);
            }
            if (!_isClosing)
            {
                _autoScanTimer.Stop();
                _autoScanTimer.Start();
            }
            return;
        }
        _savePathLostNotified = false;

        if (_isScanning) return;
        if (!isUserInitiated && !_settings.AutoRefreshLiveries) return;

        _isScanning = true;
        _autoScanTimer.Stop();

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        if (isUserInitiated)
        {
            SetLoading(true, Strings.LoadingScanning);
            RefreshButton.IsEnabled = false;
        }

        var progress = isUserInitiated ? new Progress<string>(msg => LoadingText.Text = msg) : null;

        try
        {
            var result = await _scanService.ScanAsync(saveDataPath, progress, cts.Token);
            if (cts.IsCancellationRequested) return;

            _allEntries = MergeWithLocalState(result.Entries);

            _lastScanResult = result;
            if (isUserInitiated) RenderStatus();
            RebuildTagsBar();
            RefreshGallery();
        }
        catch (OperationCanceledException)
        {
            // do not log
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(saveDataPath, $"Scan failed: '{saveDataPath}'", ex);
            if (isUserInitiated)
                StatusText.Text = string.Format(Strings.StatusScanError, ex.Message);
        }
        finally
        {
            bool isCurrentScan = ReferenceEquals(_scanCts, cts);

            if (isUserInitiated && isCurrentScan)
            {
                SetLoading(false);
                RefreshButton.IsEnabled = true;
            }

            if (isCurrentScan)
            {
                _scanCts = null;
                _isScanning = false;
                if (!_isClosing)
                {
                    _autoScanTimer.Stop();
                    _autoScanTimer.Start();
                }
            }
            cts.Dispose();
        }
    }

    private void RenderStatus()
    {
        if (_lastScanResult is null) return;
        var r = _lastScanResult;

        string text = string.Format(Strings.StatusProcessed, r.Parsed, r.ReusedFromCache);
        if (r.Errors > 0) text += string.Format(Strings.StatusErrors, r.Errors);
        if (r.Removed > 0) text += string.Format(Strings.StatusRemoved, r.Removed);
        StatusText.Text = text;
    }

    private void SetLoading(bool loading, string? text = null)
    {
        LoadingOverlay.IsVisible = loading;
        if (text is not null) LoadingText.Text = text;
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SortModeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag || !int.TryParse(tag, out int index)) return;
        _settings.SortMode = Enum.IsDefined((SortMode)index) ? (SortMode)index : SortMode.Manufacture;
        AppSettingsService.Save(_settings);
        UpdateDisplayFilterChecks();
        RefreshGallery();
    }

    private List<LiveryEntry> GetFilteredEntries() => GalleryFilterService.Apply(
        _allEntries,
        SearchBox.Text,
        _selectedTags,
        _settings.FavoriteMode == FavoriteMode.OnlyFavorites,
        _settings.DuplicatesFilterMode);

    private void RefreshGallery()
    {
        var filtered = GetFilteredEntries();
        var groups = BuildGroups(filtered);
        ReplaceGroups(groups);
        UpdateCountsAndEmptyState(filtered);
    }

    private List<LiveryGroup> BuildGroups(List<LiveryEntry> filtered) => GalleryGroupingService.Group(
        filtered,
        _settings.SortMode,
        _settings.FavoriteMode,
        _settings.GroupingEnabled,
        ComputeGroupWidth());

    private void ReplaceGroups(List<LiveryGroup> newGroups)
    {
        bool samePositionalOrder = _displayedGroups.Count == newGroups.Count;
        if (samePositionalOrder)
        {
            for (int i = 0; i < newGroups.Count; i++)
            {
                if (_displayedGroups[i].Key != newGroups[i].Key)
                {
                    samePositionalOrder = false;
                    break;
                }
            }
        }

        if (!samePositionalOrder)
        {
            foreach (var oldGroup in _displayedGroups)
                oldGroup.Dispose();
            _displayedGroups.Clear();
            foreach (var newGroup in newGroups)
                _displayedGroups.Add(newGroup);

            GroupsHost.InvalidateMeasure();
            GalleryScroll.InvalidateMeasure();
            return;
        }

        for (int i = 0; i < newGroups.Count; i++)
        {
            var oldGroup = _displayedGroups[i];
            var newGroup = newGroups[i];

            if (AreGroupsEquivalent(oldGroup, newGroup))
            {
                newGroup.Dispose();
                continue;
            }

            oldGroup.Dispose();
            _displayedGroups[i] = newGroup;
        }

        GroupsHost.InvalidateMeasure();
        GalleryScroll.InvalidateMeasure();
    }

    private static bool AreGroupsEquivalent(LiveryGroup oldGroup, LiveryGroup newGroup)
    {
        if (oldGroup.Items.Count != newGroup.Items.Count) return false;
        for (int i = 0; i < oldGroup.Items.Count; i++)
        {
            if (!AreEntriesEquivalent(oldGroup.Items[i], newGroup.Items[i])) return false;
        }
        return true;
    }

    private List<LiveryEntry> MergeWithLocalState(List<LiveryEntry> freshEntries)
    {
        var previousByPath = _allEntries.ToDictionary(e => e.FolderPath);
        var mergedEntries = new List<LiveryEntry>(freshEntries.Count);
        foreach (var newEntry in freshEntries)
        {
            if (previousByPath.TryGetValue(newEntry.FolderPath, out var previous))
            {
                newEntry.IsFavorite = previous.IsFavorite;
                newEntry.Tags = previous.Tags;

                if (AreEntriesEquivalent(previous, newEntry))
                {
                    mergedEntries.Add(previous);
                    continue;
                }
            }
            mergedEntries.Add(newEntry);
        }
        return mergedEntries;
    }

    private static bool AreEntriesEquivalent(LiveryEntry a, LiveryEntry b)
    {
        return a.FolderPath == b.FolderPath
            && a.LiveryName == b.LiveryName
            && a.Author == b.Author
            && a.CarId == b.CarId
            && a.CarManufacturerRaw == b.CarManufacturerRaw
            && a.CarModelNameRaw == b.CarModelNameRaw
            && a.CarYear == b.CarYear
            && a.CarKnown == b.CarKnown
            && a.CreatedYear == b.CreatedYear
            && a.CreatedMonth == b.CreatedMonth
            && a.DownloadDate == b.DownloadDate
            && a.ThumbnailPath == b.ThumbnailPath
            && a.IsFavorite == b.IsFavorite
            && a.DuplicateStatus == b.DuplicateStatus
            && a.CLiveryHash == b.CLiveryHash
            && (a.SectionCounts ?? []).SequenceEqual(b.SectionCounts ?? [])
            && a.Tags.SequenceEqual(b.Tags, StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateCountsAndEmptyState(List<LiveryEntry> filtered)
    {
        string search = SearchBox.Text?.Trim() ?? "";
        var stats = GalleryStatisticsService.Calculate(filtered);

        CountBaseText.Text = _allEntries.Count == 0 ? "" : string.Format(Strings.CountShowing, filtered.Count, _allEntries.Count);

        FavoritesCountPanel.IsVisible = stats.FavoritesShown > 0;
        FavoritesCountText.Text = stats.FavoritesShown.ToString();

        DuplicatesCountPanel.IsVisible = stats.DuplicatesShown > 0;
        DuplicatesCountText.Text = stats.DuplicatesShown.ToString();

        PossibleDuplicatesCountPanel.IsVisible = stats.PossibleDuplicatesShown > 0;
        PossibleDuplicatesCountText.Text = stats.PossibleDuplicatesShown.ToString();

        if (_allEntries.Count == 0)
        {
            EmptyStateText.Text = Strings.EmptyNoLiveries;
            EmptyState.IsVisible = true;
        }
        else if (filtered.Count == 0)
        {
            EmptyStateText.Text = string.Format(Strings.EmptyNoResults, search);
            EmptyState.IsVisible = true;
        }
        else
        {
            EmptyState.IsVisible = false;
        }
    }

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

    private HashSet<string>? _lastTagsBarTags;

    private void RebuildTagsBar()
    {
        var allTags = _allEntries
            .SelectMany(x => x.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allTagsSet = new HashSet<string>(allTags, StringComparer.OrdinalIgnoreCase);
        if (_lastTagsBarTags is not null && _lastTagsBarTags.SetEquals(allTagsSet)) return;
        _lastTagsBarTags = allTagsSet;

        _selectedTags.RemoveWhere(t => !allTags.Contains(t, StringComparer.OrdinalIgnoreCase));

        TagsBar.Children.Clear();
        TagsFilterRow.IsVisible = allTags.Count > 0;

        foreach (var tag in allTags)
        {
            var button = new ToggleButton
            {
                Content = tag,
                IsChecked = _selectedTags.Contains(tag),
                Margin = new Thickness(0, 0, 8, 8)
            };
            button.Classes.Add("tagChip");
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true) _selectedTags.Add(tag);
                else _selectedTags.Remove(tag);
                RefreshGallery();
            };
            TagsBar.Children.Add(button);
        }
    }

    private async void EditTags_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LiveryEntry entry) return;

        var dialog = new TagEditDialog(entry.Tags);
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            entry.Tags = dialog.ResultTags;
            _tagService.SetTags(entry.FolderName, entry.Tags);
            RebuildTagsBar();

            if (_selectedTags.Count > 0)
                RefreshGallery();
        }
    }

    private async void Card_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LiveryEntry entry) return;
        var bitmap = await ThumbnailCacheService.AcquireForAsync(control, entry.ThumbnailPath);
        if (ReferenceEquals(control.DataContext, entry))
            entry.Thumbnail = bitmap;
    }

    private void Card_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is LiveryEntry entry) entry.Thumbnail = null;
        ThumbnailCacheService.ReleaseFor(control);
    }

    private async void Card_DataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not LiveryEntry entry)
        {
            ThumbnailCacheService.ReleaseFor(control);
            return;
        }
        var bitmap = await ThumbnailCacheService.AcquireForAsync(control, entry.ThumbnailPath);
        if (ReferenceEquals(control.DataContext, entry))
            entry.Thumbnail = bitmap;
    }

    private void ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LiveryEntry entry) return;

        entry.IsFavorite = !entry.IsFavorite;
        _favoriteService.SetFavorite(entry.FolderName, entry.IsFavorite);

        if (_settings.FavoriteMode != FavoriteMode.None)
            RefreshGallery();
        else
            UpdateCountsAndEmptyState(GetFilteredEntries());
    }

    private void FavModeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag || !int.TryParse(tag, out int index)) return;
        _settings.FavoriteMode = Enum.IsDefined((FavoriteMode)index) ? (FavoriteMode)index : FavoriteMode.None;
        AppSettingsService.Save(_settings);
        UpdateDisplayFilterChecks();
        RefreshGallery();
    }

    private void DupModeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag || !int.TryParse(tag, out int index)) return;
        _settings.DuplicatesFilterMode = Enum.IsDefined((DuplicatesFilterMode)index) ? (DuplicatesFilterMode)index : DuplicatesFilterMode.All;
        AppSettingsService.Save(_settings);
        UpdateDisplayFilterChecks();
        RefreshGallery();
    }

    private void GroupingToggleItem_Click(object? sender, RoutedEventArgs e)
    {
        _settings.GroupingEnabled = !_settings.GroupingEnabled;
        AppSettingsService.Save(_settings);
        UpdateDisplayFilterChecks();
        RefreshGallery();
    }

    private void UpdateDisplayFilterChecks()
    {
        GroupingToggleItem.IsChecked = _settings.GroupingEnabled;

        SortManufacturerItem.IsChecked = _settings.SortMode == SortMode.Manufacture;
        SortAuthorItem.IsChecked = _settings.SortMode == SortMode.Author;
        SortDownloadTimeItem.IsChecked = _settings.SortMode == SortMode.DownloadTime;

        FavNoneItem.IsChecked = _settings.FavoriteMode == FavoriteMode.None;
        FavFirstItem.IsChecked = _settings.FavoriteMode == FavoriteMode.FavoritesFirst;
        FavOnlyItem.IsChecked = _settings.FavoriteMode == FavoriteMode.OnlyFavorites;
        FavSeparateItem.IsChecked = _settings.FavoriteMode == FavoriteMode.FavoritesSeparately;
        FavSeparateItem.IsEnabled = _settings.GroupingEnabled;

        DupAllItem.IsChecked = _settings.DuplicatesFilterMode == DuplicatesFilterMode.All;
        DupAndPossibleItem.IsChecked = _settings.DuplicatesFilterMode == DuplicatesFilterMode.DuplicatesAndPossible;
        DupOnlyItem.IsChecked = _settings.DuplicatesFilterMode == DuplicatesFilterMode.DuplicatesOnly;
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
        var dlg = new SettingsDialog(_settings, _savePath);
        await dlg.ShowDialog(this);
        if (dlg.SavePathChanged)
        {
            _savePath = _settings.SavePath;
            if (SaveDataPath is not null) await RunScanAsync(isUserInitiated: true);
        }
    }

    private async void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        var stats = GalleryStatisticsService.CalculateOverall(_allEntries);

        static string Format(string? label, int count) => label is not null ? $"{label} ({count})" : "-";

        string message = string.Join("\n", new[]
        {
            $"{Strings.StatsTotalLiveries}: {stats.Total}",
            $"{Strings.StatsFavoritesCount}: {stats.FavoritesCount}",
            "",
            $"{Strings.StatsPopularManufacturer}: {Format(stats.PopularManufacturer, stats.PopularManufacturerCount)}",
            $"{Strings.StatsPopularModel}: {Format(stats.PopularModel, stats.PopularModelCount)}",
            $"{Strings.StatsPopularCar}: {Format(stats.PopularCar, stats.PopularCarCount)}",
            $"{Strings.StatsPopularAuthor}: {Format(stats.PopularAuthor, stats.PopularAuthorCount)}",
            "",
            $"{Strings.StatsFavoriteManufacturer}: {Format(stats.FavoriteManufacturer, stats.FavoriteManufacturerCount)}",
            $"{Strings.StatsFavoriteModel}: {Format(stats.FavoriteModel, stats.FavoriteModelCount)}",
            $"{Strings.StatsFavoriteCar}: {Format(stats.FavoriteCar, stats.FavoriteCarCount)}",
            $"{Strings.StatsFavoriteAuthor}: {Format(stats.FavoriteAuthor, stats.FavoriteAuthorCount)}",
            "",
            $"{Strings.StatsTotalDuplicates}: {stats.DuplicatesCount}",
            $"{Strings.StatsPossibleDuplicates}: {stats.PossibleDuplicatesCount}",
        });

        await InfoDialog.ShowAsync(this, Strings.StatsTitle, message);
    }

    private void ApplyLocalizedTexts()
    {
        CustomTitleBarText.Text = Strings.AppTitle;
        MinimizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MinimizeTooltip);
        MaximizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MaximizeTooltip);
        CloseButtonEl.SetValue(ToolTip.TipProperty, Strings.CloseTooltip);

        RefreshButton.SetValue(ToolTip.TipProperty, Strings.RefreshTooltip);
        StatsButton.SetValue(ToolTip.TipProperty, Strings.StatsToggleTooltip);
        SettingsButton.SetValue(ToolTip.TipProperty, Strings.SettingsToggleTooltip);
        OpenSettingsMenuItem.Header = Strings.SettingsDialogTitle;
        ContactsMenuItem.Header = Strings.SettingsMenuContacts;
        AboutMenuItem.Header = Strings.AboutTitle;
        SearchBox.PlaceholderText = Strings.SearchPlaceholder;

        DisplayFilterButton.SetValue(ToolTip.TipProperty, Strings.DisplayFilterTooltip);
        GroupingToggleItem.Header = Strings.GroupingToggleLabel;
        SortManufacturerItem.Header = Strings.SortManufacturer;
        SortAuthorItem.Header = Strings.SortAuthor;
        SortDownloadTimeItem.Header = Strings.SortDownloadDate;
        FavNoneItem.Header = Strings.NormalOrderToggle;
        FavFirstItemText.Text = Strings.FavoritesFirstToggle;
        FavOnlyItemText.Text = Strings.OnlyFavoritesToggle;
        FavSeparateItemText.Text = Strings.SeparateFavoritesToggle;
        DupAllItem.Header = Strings.DuplicatesFilterAll;
        DupAndPossibleItem.Header = Strings.DuplicatesFilterAndPossible;
        DupOnlyItem.Header = Strings.DuplicatesFilterOnly;

        TagsFilterLabel.Text = Strings.TagsFilterLabel;
    }

    internal void OnLanguageChanged()
    {
        if (!_isLoaded) return;
        ApplyLocalizedTexts();
        RenderStatus();
        UpdateCountsAndEmptyState(GetFilteredEntries());
        foreach (var entry in _allEntries)
            entry.RefreshLocalizedText();

        if (GroupsHost.ItemsSource is IEnumerable<LiveryGroup> currentGroups)
        {
            foreach (var group in currentGroups)
            {
                group.Key = group.SpecialKind switch
                {
                    LiveryGroupSpecialKind.DownloadMonth when group.SpecialMonth is { } month =>
                        month.ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture),
                    LiveryGroupSpecialKind.UnknownDownloadDate => Strings.UnknownDownloadDate,
                    LiveryGroupSpecialKind.UnknownManufacturer => Strings.UnknownManufacturer,
                    LiveryGroupSpecialKind.AllLiveries => Strings.AllLiveriesGroupName,
                    _ when group.IsFavoritesGroup => Strings.SeparateFavoritesGroupName,
                    _ => group.Key
                };
            }
        }

        if (_latestVersion is not null)
            UpdateBannerText.Text = string.Format(Strings.UpdateAvailableFormat, _latestVersion);
    }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }
}
