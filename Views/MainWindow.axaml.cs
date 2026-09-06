using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class MainWindow : Window
{
    private readonly AppCacheService _cacheService = new();
    private readonly CarDatabaseService _carDb = new();
    private readonly TagService _tagService = new();
    private readonly FavoriteService _favoriteService = new();
    private readonly LiveryScanService _scanService;

    private List<LiveryEntry> _allEntries = new();
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private string? _savePath;
    private string? SaveDataPath =>
        _savePath is null ? null : LocalSaveService.GetSaveDataPath(_savePath);

    private CancellationTokenSource? _scanCts;
    private readonly AppSettingsData _settings = AppSettingsService.Load();

    private LiveryScanEntry? _lastScanResult;
    private string? _updateReleaseUrl;
    private string? _latestVersion;
    private string? _updateReleaseBody;

    private bool _isLoaded;
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _resizeDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow()
    {
        InitializeComponent();
        _scanService = new LiveryScanService(_cacheService, _carDb, _favoriteService, _tagService);

        UpdateThemeIcon();

        if (!Enum.IsDefined(_settings.SortMode)) _settings.SortMode = SortMode.Manufacture;
        if (!Enum.IsDefined(_settings.FavoriteMode)) _settings.FavoriteMode = FavoriteMode.None;
        if (!Enum.IsDefined(_settings.DuplicatesFilterMode)) _settings.DuplicatesFilterMode = DuplicatesFilterMode.All;
        UpdateDisplayFilterChecks();

        ApplyLocalizedTexts();

        _searchDebounceTimer.Tick += (_, __) =>
        {
            _searchDebounceTimer.Stop();
            ApplyFilterAndSort();
        };

        _resizeDebounceTimer.Tick += (_, __) =>
        {
            _resizeDebounceTimer.Stop();
            if (_isLoaded) ApplyFilterAndSort();
        };
        SizeChanged += (_, __) =>
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        };

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _ = CheckForUpdatesAsync();

        _carDb.LoadLocal();

        bool savedPathMissing = !string.IsNullOrWhiteSpace(_settings.SavePath)
            && !Directory.Exists(_settings.SavePath);

        _savePath = !string.IsNullOrWhiteSpace(_settings.SavePath) && Directory.Exists(_settings.SavePath)
            ? _settings.SavePath
            : LocalSaveService.FindLocalSavePath();

        await RefreshCarDatabaseAsync();

        if (savedPathMissing)
        {
            await InfoDialog.ShowAsync(this, Strings.FolderNotFoundTitle, Strings.SavedPathNotFoundNotice);
        }

        if (_savePath is null)
        {
            await PromptForSavePathAsync(initial: true);
            return;
        }

        await RunScanAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var result = await AppUpdateCheckService.CheckAsync();
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

    private async Task RefreshCarDatabaseAsync()
    {
        SetLoading(true, Strings.CarDbUpdating);
        try
        {
            bool changed = await _carDb.RefreshAsync();
            string countText = $"{_carDb.Count} {Strings.CarDbCarsWord}";
            if (_carDb.LastError is not null)
            {
                StatusText.Text = _carDb.HasLocalData
                    ? string.Format(Strings.CarDbDownloadFailed, countText)
                    : Strings.CarDbNoDataAtAll;
            }
            else
            {
                StatusText.Text = changed
                    ? string.Format(Strings.CarDbUpdated, countText)
                    : string.Format(Strings.CarDbUpToDate, countText);
            }
        }
        finally
        {
            SetLoading(false);
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
            await RunScanAsync();
            return;
        }
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshCarDatabaseAsync();

        if (_savePath is null)
        {
            await PromptForSavePathAsync(initial: true);
            return;
        }
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        if (SaveDataPath is null) return;

        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        SetLoading(true, Strings.LoadingScanning);
        RefreshButton.IsEnabled = false;

        var progress = new Progress<string>(msg => LoadingText.Text = msg);

        try
        {
            var result = await _scanService.ScanAsync(SaveDataPath, progress, cts.Token);
            if (cts.IsCancellationRequested) return;

            _allEntries = result.Entries;
            _lastScanResult = result;
            RenderStatus();
            RebuildTagsBar();
            ApplyFilterAndSort();
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Strings.StatusScanError, ex.Message);
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                SetLoading(false);
                RefreshButton.IsEnabled = true;
            }
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
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        string search = SearchBox.Text?.Trim() ?? "";
        bool onlyFavorites = _settings.FavoriteMode == FavoriteMode.OnlyFavorites;
        bool favoritesFirst = _settings.FavoriteMode == FavoriteMode.FavoritesFirst;
        bool separateFavorites = _settings.FavoriteMode == FavoriteMode.FavoritesSeparately;

        var duplicatesFilterMode = _settings.DuplicatesFilterMode;

        IEnumerable<LiveryEntry> query = _allEntries;
        if (search.Length > 0)
            query = query.Where(x => x.MatchesSearch(search));

        if (_selectedTags.Count > 0)
            query = query.Where(x => _selectedTags.All(t => x.Tags.Any(xt => xt.Equals(t, StringComparison.OrdinalIgnoreCase))));

        if (onlyFavorites)
            query = query.Where(x => x.IsFavorite);

        query = duplicatesFilterMode switch
        {
            DuplicatesFilterMode.DuplicatesOnly => query.Where(x => x.IsDuplicate),
            DuplicatesFilterMode.DuplicatesAndPossible => query.Where(x => x.IsDuplicate || x.IsPossibleDuplicate),
            _ => query
        };

        var filtered = query.ToList();
        double groupWidth = ComputeGroupWidth();

        List<LiveryGroup> groups;

        if (separateFavorites)
        {
            var favoriteItems = filtered.Where(x => x.IsFavorite).ToList();
            var restItems = onlyFavorites ? new List<LiveryEntry>() : filtered.Where(x => !x.IsFavorite).ToList();

            groups = new List<LiveryGroup>();
            if (favoriteItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateFavoritesGroupName,
                    Items = SortForCurrentMode(favoriteItems),
                    GroupWidth = groupWidth
                });
            }
            groups.AddRange(BuildGroups(restItems, favoritesFirst: false, groupWidth));
        }
        else
        {
            groups = BuildGroups(filtered, favoritesFirst, groupWidth);
        }

        GroupsHost.ItemsSource = groups;
        GroupsHost.InvalidateMeasure();
        GalleryScroll.InvalidateMeasure();

        int favoritesShown = filtered.Count(x => x.IsFavorite);
        int duplicatesShown = filtered.Count(x => x.IsDuplicate);
        int possibleDuplicatesShown = filtered.Count(x => x.IsPossibleDuplicate);
        CountText.Text = _allEntries.Count == 0
            ? ""
            : string.Format(Strings.CountShowing, filtered.Count, _allEntries.Count)
              + (favoritesShown > 0 ? string.Format(Strings.FavoritesCountFormat, favoritesShown) : "")
              + (duplicatesShown > 0 ? string.Format(Strings.DuplicatesCountFormat, duplicatesShown) : "")
              + (possibleDuplicatesShown > 0 ? string.Format(Strings.PossibleDuplicatesCountFormat, possibleDuplicatesShown) : "");

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

    private List<LiveryGroup> BuildGroups(List<LiveryEntry> items, bool favoritesFirst, double groupWidth)
    {
        if (_settings.SortMode == SortMode.Author)
        {
            return [.. items
                .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new LiveryGroup
                {
                    Key = g.Key,
                    Items = OrderGroupItems(g, favoritesFirst,
                            x => x.CarManufacturer, x => x.CarModelName, x => x.LiveryName),
                    GroupWidth = groupWidth
                })];
        }

        if (_settings.SortMode == SortMode.DownloadTime)
        {
            return [.. items
                .GroupBy(x => x.DownloadYearMonth)
                .OrderByDescending(g => g.Key ?? DateTime.MinValue)
                .Select(g => new LiveryGroup
                {
                    Key = g.Key is { } month
                        ? month.ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture)
                        : Strings.UnknownDownloadDate,
                    Items = (favoritesFirst
                                ? g.OrderByDescending(x => x.IsFavorite).ThenByDescending(x => x.DownloadDate ?? DateTime.MinValue)
                                : g.OrderByDescending(x => x.DownloadDate ?? DateTime.MinValue))
                            .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                    GroupWidth = groupWidth
                })];
        }

        string unknownLabel = Strings.UnknownManufacturer;
        return [.. items
            .GroupBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Equals(unknownLabel, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LiveryGroup
            {
                Key = g.Key,
                Items = [.. (favoritesFirst
                            ? g.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                            : g.OrderBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase))
                        .ThenBy(x => x.CarYear)
                        .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
                GroupWidth = groupWidth
            })];
    }

    private List<LiveryEntry> SortForCurrentMode(List<LiveryEntry> items) => _settings.SortMode switch
    {
        SortMode.Author =>
            [.. items.OrderBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
        SortMode.DownloadTime =>
            [.. items.OrderByDescending(x => x.DownloadDate ?? DateTime.MinValue)
                  .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
        _ => [.. items.OrderBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarYear)
                  .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
    };

    private static List<LiveryEntry> OrderGroupItems(
        IEnumerable<LiveryEntry> items,
        bool favoritesFirst,
        Func<LiveryEntry, string> key1,
        Func<LiveryEntry, string> key2,
        Func<LiveryEntry, string> key3)
    {
        var ordered = favoritesFirst
            ? items.OrderByDescending(x => x.IsFavorite).ThenBy(key1, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(key1, StringComparer.OrdinalIgnoreCase);

        return ordered
            .ThenBy(key2, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key3, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ThemeToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        AppThemeService.ToggleTheme();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        string key = AppThemeService.IsDarkTheme ? "IconBrightness" : "IconMoon";
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var res) == true && res is Geometry geometry)
            ThemeIconPath.Data = geometry;
    }

    private void RebuildTagsBar()
    {
        var allTags = _allEntries
            .SelectMany(x => x.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
                ApplyFilterAndSort();
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
            ApplyFilterAndSort();
        }
    }

    private void ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not LiveryEntry entry) return;

        entry.IsFavorite = !entry.IsFavorite;
        _favoriteService.SetFavorite(entry.FolderName, entry.IsFavorite);
        ApplyFilterAndSort();
    }

    private void FavModeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag || !int.TryParse(tag, out int index)) return;
        _settings.FavoriteMode = Enum.IsDefined((FavoriteMode)index) ? (FavoriteMode)index : FavoriteMode.None;
        AppSettingsService.Save(_settings);
        UpdateDisplayFilterChecks();
        ApplyFilterAndSort();
    }

    private void DupModeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag || !int.TryParse(tag, out int index)) return;
        _settings.DuplicatesFilterMode = Enum.IsDefined((DuplicatesFilterMode)index) ? (DuplicatesFilterMode)index : DuplicatesFilterMode.All;
        AppSettingsService.Save(_settings);
        UpdateDisplayFilterChecks();
        ApplyFilterAndSort();
    }

    private void UpdateDisplayFilterChecks()
    {
        SortManufacturerItem.IsChecked = _settings.SortMode == SortMode.Manufacture;
        SortAuthorItem.IsChecked = _settings.SortMode == SortMode.Author;
        SortDownloadTimeItem.IsChecked = _settings.SortMode == SortMode.DownloadTime;

        FavNoneItem.IsChecked = _settings.FavoriteMode == FavoriteMode.None;
        FavFirstItem.IsChecked = _settings.FavoriteMode == FavoriteMode.FavoritesFirst;
        FavOnlyItem.IsChecked = _settings.FavoriteMode == FavoriteMode.OnlyFavorites;
        FavSeparateItem.IsChecked = _settings.FavoriteMode == FavoriteMode.FavoritesSeparately;

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

    private async void PathsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.Flyout?.Hide();
        var dlg = new PathsDialog(_settings);
        await dlg.ShowDialog(this);

        if (dlg.SavePathChanged)
        {
            _savePath = _settings.SavePath;
            if (SaveDataPath is not null) await RunScanAsync();
        }
    }

    private async void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        int total = _allEntries.Count;
        string favoriteManufacturer = "-";
        string favoriteAuthor = "-";

        if (total > 0)
        {
            var topManufacturer = _allEntries
                .GroupBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First();
            favoriteManufacturer = $"{topManufacturer.Key} ({topManufacturer.Count()})";

            var topAuthor = _allEntries
                .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First();
            favoriteAuthor = $"{topAuthor.Key} ({topAuthor.Count()})";
        }

        int favoritesCount = _allEntries.Count(x => x.IsFavorite);
        int duplicatesCount = _allEntries.Count(x => x.IsDuplicate);
        int possibleDuplicatesCount = _allEntries.Count(x => x.IsPossibleDuplicate);

        string message = string.Join("\n", new[]
        {
            $"{Strings.StatsTotalLiveries}: {total}",
            $"{Strings.StatsFavoritesCount}: {favoritesCount}",
            "",
            $"{Strings.StatsFavoriteManufacturer}: {favoriteManufacturer}",
            $"{Strings.StatsFavoriteAuthor}: {favoriteAuthor}",
            "",
            $"{Strings.StatsTotalDuplicates}: {duplicatesCount}",
            $"{Strings.StatsPossibleDuplicates}: {possibleDuplicatesCount}",
        });

        await InfoDialog.ShowAsync(this, Strings.StatsTitle, message);
    }

    private void LanguageItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string code) return;

        AppLanguage language = code switch
        {
            "ru" => AppLanguage.Russian,
            "en" => AppLanguage.English,
            "ja" => AppLanguage.Japanese,
            "de" => AppLanguage.German,
            "fr" => AppLanguage.French,
            "zh-Hant" => AppLanguage.ChineseTraditional,
            "zh-Hans" => AppLanguage.ChineseSimplified,
            "ko" => AppLanguage.Korean,
            "es" => AppLanguage.Spanish,
            "it" => AppLanguage.Italian,
            "pt" => AppLanguage.Portuguese,
            _ => AppLanguage.English,
        };

        AppLocalisationService.AppLanguage = language;
        _settings.Language = language;
        AppSettingsService.Save(_settings);

        SettingsButton.Flyout?.Hide();
        OnLanguageChanged();
    }

    private void ApplyLocalizedTexts()
    {
        CustomTitleBarText.Text = Strings.AppTitle;
        MinimizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MinimizeTooltip);
        MaximizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MaximizeTooltip);
        CloseButtonEl.SetValue(ToolTip.TipProperty, Strings.CloseTooltip);

        RefreshButton.SetValue(ToolTip.TipProperty, Strings.RefreshTooltip);
        StatsButton.SetValue(ToolTip.TipProperty, Strings.StatsToggleTooltip);
        ThemeToggleButton.SetValue(ToolTip.TipProperty, Strings.ThemeToggleTooltip);
        SettingsButton.SetValue(ToolTip.TipProperty, Strings.SettingsToggleTooltip);
        LanguageMenuItem.Header = Strings.LanguageMenuLabel;
        PathsMenuItem.Header = Strings.SettingsMenuPaths;
        ContactsMenuItem.Header = Strings.SettingsMenuContacts;
        AboutMenuItem.Header = Strings.AboutTitle;
        SearchBox.PlaceholderText = Strings.SearchPlaceholder;

        DisplayFilterButton.SetValue(ToolTip.TipProperty, Strings.DisplayFilterTooltip);
        SortManufacturerItem.Header = Strings.SortManufacturer;
        SortAuthorItem.Header = Strings.SortAuthor;
        SortDownloadTimeItem.Header = Strings.SortDownloadDate;
        FavNoneItem.Header = Strings.NormalOrderToggle;
        FavFirstItem.Header = Strings.FavoritesFirstToggle;
        FavOnlyItem.Header = Strings.OnlyFavoritesToggle;
        FavSeparateItem.Header = Strings.SeparateFavoritesToggle;
        DupAllItem.Header = Strings.DuplicatesFilterAll;
        DupAndPossibleItem.Header = Strings.DuplicatesFilterAndPossible;
        DupOnlyItem.Header = Strings.DuplicatesFilterOnly;

        TagsFilterLabel.Text = Strings.TagsFilterLabel;
    }

    private void OnLanguageChanged()
    {
        if (!_isLoaded) return;
        ApplyLocalizedTexts();
        RenderStatus();
        ApplyFilterAndSort();

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
