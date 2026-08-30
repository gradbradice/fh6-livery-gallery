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
    private CancellationTokenSource? _scanCts;
    private readonly AppSettingsData _settings = AppSettingsService.Load();

    private LiveryScanEntry? _lastScanResult;
    private string? _updateReleaseUrl;
    private string? _latestVersion;
    private string? _updateReleaseBody;

    private bool _isLoaded;
    private bool _suppressComboEvents;
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _resizeDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow()
    {
        InitializeComponent();
        _scanService = new LiveryScanService(_cacheService, _carDb, _favoriteService, _tagService);

        UpdateThemeIcon();

        SortCombo.SelectedIndex = Enum.IsDefined(_settings.SortMode)
            ? (int)_settings.SortMode
            : (int)SortMode.Manufacture;

        FavoritesModeCombo.SelectedIndex = Enum.IsDefined(_settings.FavoriteMode)
            ? (int)_settings.FavoriteMode
            : (int)FavoriteMode.None;

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

        _savePath = !string.IsNullOrWhiteSpace(_settings.SavePath) && Directory.Exists(_settings.SavePath)
            ? _settings.SavePath
            : LocalFileService.FindLocalFilesPath();

        await RefreshCarDatabaseAsync();

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

        var result = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.SelectFolderDialogTitle,
            AllowMultiple = false
        });

        var folder = result.Count > 0 ? result[0] : null;
        string? path = folder?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        _savePath = path;
        _settings.SavePath = _savePath;
        AppSettingsService.Save(_settings);
        await RunScanAsync();
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
        if (_savePath is null) return;

        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        SetLoading(true, Strings.LoadingScanning);
        RefreshButton.IsEnabled = false;

        var progress = new Progress<string>(msg => LoadingText.Text = msg);

        try
        {
            var result = await _scanService.ScanAsync(_savePath, progress, cts.Token);
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

    private void SortCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || _suppressComboEvents) return;
        _settings.SortMode = (SortMode)SortCombo.SelectedIndex;
        AppSettingsService.Save(_settings);
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        string search = SearchBox.Text?.Trim() ?? "";
        bool onlyFavorites = FavoritesModeCombo.SelectedIndex == (int)FavoriteMode.OnlyFavorites;
        bool favoritesFirst = FavoritesModeCombo.SelectedIndex == (int)FavoriteMode.FavoritesFirst;
        bool separateFavorites = FavoritesModeCombo.SelectedIndex == (int)FavoriteMode.FavoritesSeparately;

        IEnumerable<LiveryEntry> query = _allEntries;
        if (search.Length > 0)
            query = query.Where(x => x.MatchesSearch(search));

        if (_selectedTags.Count > 0)
            query = query.Where(x => _selectedTags.All(t => x.Tags.Any(xt => xt.Equals(t, StringComparison.OrdinalIgnoreCase))));

        if (onlyFavorites)
            query = query.Where(x => x.IsFavorite);

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
        CountText.Text = _allEntries.Count == 0
            ? ""
            : string.Format(Strings.CountShowing, filtered.Count, _allEntries.Count)
              + (favoritesShown > 0 ? string.Format(Strings.FavoritesCountFormat, favoritesShown) : "");

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
        if (SortCombo.SelectedIndex == (int)SortMode.Author)
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

        if (SortCombo.SelectedIndex == (int)SortMode.DownloadTime)
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

    private List<LiveryEntry> SortForCurrentMode(List<LiveryEntry> items) => (SortMode)SortCombo.SelectedIndex switch
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

    private void FavoritesModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || _suppressComboEvents) return;
        _settings.FavoriteMode = Enum.IsDefined((FavoriteMode)FavoritesModeCombo.SelectedIndex)
            ? (FavoriteMode)FavoritesModeCombo.SelectedIndex
            : FavoriteMode.None;
        AppSettingsService.Save(_settings);
        ApplyFilterAndSort();
    }

    private async void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog();
        await dlg.ShowDialog(this);
    }

    private void LanguageItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button item || item.Tag is not string code) return;

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

        LanguageButton.Flyout?.Hide();
        OnLanguageChanged();
    }

    private void ApplyLocalizedTexts()
    {
        CustomTitleBarText.Text = Strings.AppTitle;
        MinimizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MinimizeTooltip);
        MaximizeButtonEl.SetValue(ToolTip.TipProperty, Strings.MaximizeTooltip);
        CloseButtonEl.SetValue(ToolTip.TipProperty, Strings.CloseTooltip);

        RefreshButton.SetValue(ToolTip.TipProperty, Strings.RefreshTooltip);
        LanguageButton.SetValue(ToolTip.TipProperty, Strings.LanguageToggleTooltip);
        ThemeToggleButton.SetValue(ToolTip.TipProperty, Strings.ThemeToggleTooltip);
        AboutButton.SetValue(ToolTip.TipProperty, Strings.AboutTooltip);
        SearchBox.PlaceholderText = Strings.SearchPlaceholder;

        SetComboItemText(SortCombo, (int)SortMode.Manufacture, Strings.SortManufacturer);
        SetComboItemText(SortCombo, (int)SortMode.Author, Strings.SortAuthor);
        SetComboItemText(SortCombo, (int)SortMode.DownloadTime, Strings.SortDownloadDate);
        RefreshComboClosedDisplay(SortCombo);

        SetComboItemText(FavoritesModeCombo, (int)FavoriteMode.None, Strings.NormalOrderToggle);
        SetComboItemText(FavoritesModeCombo, (int)FavoriteMode.FavoritesFirst, Strings.FavoritesFirstToggle);
        SetComboItemText(FavoritesModeCombo, (int)FavoriteMode.OnlyFavorites, Strings.OnlyFavoritesToggle);
        SetComboItemText(FavoritesModeCombo, (int)FavoriteMode.FavoritesSeparately, Strings.SeparateFavoritesToggle);
        RefreshComboClosedDisplay(FavoritesModeCombo);

        TagsFilterLabel.Text = Strings.TagsFilterLabel;
    }

    private static void SetComboItemText(ComboBox combo, int index, string text)
    {
        if (combo.Items.Cast<object>().ElementAtOrDefault(index) is ComboBoxItem item)
            item.Content = text;
    }

    private void RefreshComboClosedDisplay(ComboBox combo)
    {
        int index = combo.SelectedIndex;
        if (index < 0) return;

        _suppressComboEvents = true;
        combo.SelectedIndex = -1;
        combo.SelectedIndex = index;
        _suppressComboEvents = false;
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
