using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveryGallery.Controller;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;
using LiveryGallery.Services.Diagnostics;
using LiveryGallery.ViewModels;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Views;

internal partial class SettingsDialog : Window
{
    /// <summary>The page the dialog opened on last time, for the rest of this run</summary>
    private static SettingsPage _lastPage = SettingsPage.Appearance;

    private readonly SettingsViewModel _viewModel;
    private readonly GameProfile _profile;
    private readonly Func<Stream, CancellationToken, Task>? _exportDiagnostics;
    private readonly AccountDataActions? _accountData;
    private readonly BackgroundTaskTracker _tasks = new();
    private Task<bool>? _saveInProgress;
    private bool _forceClose;
    private bool _isClosing;
    private bool _closed;
    private SettingsPage _page = _lastPage;
    private bool _updatingPageList;

    public bool SavePathChanged => _viewModel.SavePathChanged;

    /// <param name="profile">The game: its saves and installation, and which pages it has (find in game, server)</param>
    /// <param name="exportDiagnostics">Writes the diagnostics ZIP to the stream; null hides the button</param>
    /// <param name="accountData">Lists and forgets stored account data; null hides the section</param>
    /// <param name="carTextLanguages">The game languages car names can be shown in (read from the game)</param>
    public SettingsDialog(
        AppSettingsData settings, string? resolvedSavePath, IGameFolderCheck gameFolderCheck, GameProfile profile,
        Func<Stream, CancellationToken, Task>? exportDiagnostics = null,
        AccountDataActions? accountData = null,
        IReadOnlyList<ForzaToolkit.StringTables.GameLanguage>? carTextLanguages = null)
    {
        InitializeComponent();
        _profile = profile;
        _exportDiagnostics = exportDiagnostics;
        _accountData = accountData;
        DiagnosticsSection.IsVisible = exportDiagnostics is not null;
        _viewModel = new SettingsViewModel(settings, resolvedSavePath, gameFolderCheck, profile, carTextLanguages);
        DataContext = _viewModel;

        ApplyLocalizedTexts();
        if (_accountData is not null) Opened += (_, _) => _ = _tasks.Run(LoadStoredAccountsAsync, "settings: stored accounts");
        UpdateHintDisplay();
        UpdateGameFolderStatusDisplay();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.HintIsError)) UpdateHintDisplay();
            if (e.PropertyName is nameof(SettingsViewModel.GameFolderStatusIsError)) UpdateGameFolderStatusDisplay();
        };

        Closing += SettingsDialog_Closing;
        if (string.IsNullOrEmpty(_viewModel.GamePath))
            _ = PopulateDiscoveredGamePathAsync();
    }

    private async Task PopulateDiscoveredGamePathAsync()
    {
        try
        {
            string? discovered = await _profile.FindInstallFolderAsync();
            if (discovered is null || _closed) return;
            if (!string.IsNullOrEmpty(_viewModel.GamePath)) return;

            _viewModel.GamePath = discovered;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception in PopulateDiscoveredGamePathAsync", ex);
        }
    }

    private void ApplyLocalizedTexts()
    {
        Title = Strings.SettingsDialogTitle;
        TitleBarText.Text = Strings.SettingsDialogTitle;
        TitleText.Text = Strings.SettingsDialogTitle;
        ThemeSectionLabel.Text = Strings.SettingsSectionTheme;
        LanguageSectionLabel.Text = Strings.SettingsSectionLanguage;
        CarTextLanguageSectionLabel.Text = Strings.SettingsSectionCarTextLanguage;
        CarTextLanguageHintText.Text = _viewModel.HasGameCarTexts ? Strings.CarTextLanguageHint : Strings.CarTextLanguageNoGameTexts;
        AccountDataSectionLabel.Text = Strings.AccountDataSectionLabel;
        AccountDataHint.Text = Strings.AccountDataHint;
        GamePathLabel.Text = Strings.SettingsMenuGamePath;
        SavePathLabel.Text = Strings.SavePathFieldLabel;
        ScanningSectionLabel.Text = Strings.SettingsSectionScanning;
        AutoRefreshLiveriesCheckBox.Content = Strings.AutoRefreshLiveriesLabel;
        RefreshOnButtonClickCheckBox.Content = Strings.RefreshOnButtonClickLabel;
        SearchByFolderNameCheckBox.Content = Strings.SearchByFolderNameLabel;
        DiagnosticsSectionLabel.Text = Strings.DiagnosticsSectionLabel;
        DiagnosticsHint.Text = Strings.DiagnosticsHint;
        ExportDiagnosticsButton.Content = Strings.ExportDiagnosticsButton;
        ShowChangesSinceLastLaunchCheckBox.Content = Strings.ShowChangesSinceLastLaunchLabel;
        ServerConnectionCheckBox.Content = Strings.ServerConnectionLabel;
        ServerConnectionDescription.Text = Strings.ServerConnectionDescription;
        DuplicatesSectionLabel.Text = Strings.SettingsSectionDuplicates;
        PossibleDuplicateThresholdLabel.Text = Strings.PossibleDuplicateThresholdLabel;
        PossibleDuplicateThresholdHint.Text = Strings.PossibleDuplicateThresholdHint;
        NavigationRowsLabel.Text = Strings.NavigationRowsLabel;
        NavigationKeyIntervalLabel.Text = Strings.NavigationKeyIntervalLabel;
        NavigationKeyIntervalHint.Text = Strings.NavigationKeyIntervalHint;
        SystemThemeRadio.Content = Strings.ThemeSystemLabel;
        LightThemeRadio.Content = Strings.ThemeLightLabel;
        DarkThemeRadio.Content = Strings.ThemeDarkLabel;
        ExitButton.Content = Strings.ButtonExit;
        SaveButton.Content = Strings.ButtonSave;
        UpdatePageList();
    }

    /// <summary>The pages there is something to show on, with their names in the current language</summary>
    private List<SettingsPageItem> AvailablePages()
    {
        var pages = new List<SettingsPageItem>
        {
            new(SettingsPage.Appearance, Strings.SettingsPageAppearance, this.GetThemeGeometry("IconPalette")),
            new(SettingsPage.Folders, Strings.SettingsMenuPaths, this.GetThemeGeometry("IconFolder")),
            new(SettingsPage.Gallery, Strings.SettingsPageGallery, this.GetThemeGeometry("IconPreview")),
        };
        // Pages of what this game has
        if (_profile.MyDesigns is not null)
            pages.Add(new(SettingsPage.FindInGame, Strings.SettingsSectionNavigation, this.GetThemeGeometry("IconLocate")));
        if (_profile.Features.LiveryServer)
            pages.Add(new(SettingsPage.Server, Strings.SettingsSectionServer, this.GetThemeGeometry("IconGlobe")));
        if (AccountDataSection.IsVisible || DiagnosticsSection.IsVisible)
            pages.Add(new(SettingsPage.Data, Strings.SettingsPageData, this.GetThemeGeometry("IconInfo")));
        return pages;
    }

    /// <summary>Fills the list on the left (again after a language change) and shows the current page</summary>
    private void UpdatePageList()
    {
        var pages = AvailablePages();
        var current = pages.FirstOrDefault(p => p.Page == _page) ?? pages[0];
        _updatingPageList = true;
        try
        {
            PageList.ItemsSource = pages;
            PageList.SelectedItem = current;
        }
        finally
        {
            _updatingPageList = false;
        }
        ShowPage(current);
    }

    /// <summary>Opens <paramref name="page"/>, as if picked in the list</summary>
    private void SelectPage(SettingsPage page)
    {
        if (PageList.ItemsSource is not IEnumerable<SettingsPageItem> pages) return;
        if (pages.FirstOrDefault(p => p.Page == page) is { } item) PageList.SelectedItem = item;
    }

    private void PageList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPageList || PageList.SelectedItem is not SettingsPageItem item) return;
        ShowPage(item);
    }

    private void ShowPage(SettingsPageItem item)
    {
        bool changed = item.Page != _page;
        _page = _lastPage = item.Page;
        PageTitleText.Text = item.Title;
        AppearancePage.IsVisible = item.Page == SettingsPage.Appearance;
        FoldersPage.IsVisible = item.Page == SettingsPage.Folders;
        GalleryPage.IsVisible = item.Page == SettingsPage.Gallery;
        FindInGamePage.IsVisible = item.Page == SettingsPage.FindInGame;
        ServerPage.IsVisible = item.Page == SettingsPage.Server;
        DataPage.IsVisible = item.Page == SettingsPage.Data;
        if (changed) PageScroll.Offset = default;
    }

    private void UpdateHintDisplay()
    {
        if (_viewModel.HintIsError)
        {
            HintText.Text = Strings.SaveFolderValidationFailed;
            HintText.Foreground = this.GetThemeBrush("DangerBrush");
        }
        else
        {
            // The usual place of the saves, for a game that keeps them in a PC GameSave folder
            HintText.Text = _profile.Game.Saves.PcGameSaveGameId is { } id
                ? Strings.PathFieldAutoHint + ". " + string.Format(Strings.SaveFolderContentHintFormat, id)
                : Strings.PathFieldAutoHint;
            HintText.Foreground = this.GetThemeBrush("TextSecondaryBrush");
        }
    }

    private void UpdateGameFolderStatusDisplay() =>
        GameFolderStatusText.Foreground = this.GetThemeBrush(_viewModel.GameFolderStatusIsError ? "DangerBrush" : "TextSecondaryBrush");

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private bool _askingServerConsent;

    private void ServerConnectionCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_askingServerConsent || ServerConnectionCheckBox.IsChecked != true || _viewModel.IsServerConnectionSaved) return;
        _ = _tasks.Run(AskServerConsentAsync, "server consent");
    }

    private async Task AskServerConsentAsync()
    {
        _askingServerConsent = true;
        bool agreed = false;
        try
        {
            agreed = await ConfirmDialog.AskAsync(this, Strings.ServerConsentTitle, Strings.ServerConsentMessage,
                Strings.ServerConsentAccept, Strings.ButtonCancel, "IconGlobe");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to show the livery server consent dialog", ex);
        }
        finally
        {
            if (!agreed)
            {
                ServerConnectionCheckBox.IsChecked = false;
                _viewModel.ServerConnectionEnabled = false;
            }
            _askingServerConsent = false;
        }
    }

    private void GameBrowseButton_Click(object? sender, RoutedEventArgs e) =>
        _ = _tasks.Run(async () =>
        {
            string? path = await BrowseForFolderAsync(Strings.SelectGameFolderDialogTitle);
            if (path is not null) _viewModel.GamePath = path;
        }, "browse for game folder");

    private void SaveBrowseButton_Click(object? sender, RoutedEventArgs e) =>
        _ = _tasks.Run(async () =>
        {
            string? path = await BrowseForFolderAsync(Strings.SelectFolderDialogTitle);
            if (path is not null) _viewModel.SavePath = path;
        }, "browse for save folder");

    private async Task LoadStoredAccountsAsync()
    {
        if (_accountData is null) return;
        var accounts = await _accountData.List();
        if (_closed) return;
        _viewModel.SetStoredAccounts(accounts);
        // Only the open account: nothing to forget, and the section would only take space
        bool show = accounts.Any(a => !a.IsCurrent);
        if (show == AccountDataSection.IsVisible) return;
        AccountDataSection.IsVisible = show;
        UpdatePageList(); // the data page may appear or go
    }

    private void ForgetAccountButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StoredAccountRowViewModel row)
            _ = _tasks.Run(() => ForgetAccountAsync(row), "settings: forget account");
    }

    private async Task ForgetAccountAsync(StoredAccountRowViewModel row)
    {
        if (_accountData is null) return;
        bool confirmed = await ConfirmDialog.AskAsync(
            this, Strings.AccountDataForgetTitle, string.Format(Strings.AccountDataForgetMessage, row.Title, row.SizeText));
        if (!confirmed) return;

        if (!await _accountData.Forget(row.Account.Key))
            await InfoDialog.ShowAsync(this, Strings.AccountDataForgetTitle, Strings.AccountDataForgetFailedMessage);
        await LoadStoredAccountsAsync();
    }

    private void ExportDiagnosticsButton_Click(object? sender, RoutedEventArgs e) =>
        _ = _tasks.Run(ExportDiagnosticsAsync, "export diagnostics");

    private async Task ExportDiagnosticsAsync()
    {
        if (_exportDiagnostics is null || StorageProvider is not { } provider) return;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.DiagnosticsSaveDialogTitle,
            SuggestedFileName = DiagnosticsExportService.SuggestedFileName(DateTime.Now),
            DefaultExtension = "zip",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(Strings.DiagnosticsZipFileType) { Patterns = ["*.zip"] }],
        });
        if (file is null) return;

        string? localPath = file.TryGetLocalPath();
        string shownName = localPath ?? file.Name;
        ExportDiagnosticsButton.IsEnabled = false;
        try
        {
            await using (var stream = await file.OpenWriteAsync())
                await _exportDiagnostics(stream, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to export diagnostics", ex);
            await InfoDialog.ShowAsync(this, Strings.DiagnosticsSectionLabel, Strings.DiagnosticsExportFailedMessage);
            return;
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
            file.Dispose();
        }

        string message = string.Format(Strings.DiagnosticsExportedFormat, shownName);
        if (localPath is null)
        {
            await InfoDialog.ShowAsync(this, Strings.DiagnosticsSectionLabel, message);
            return;
        }
        if (await InfoDialog.ShowWithActionAsync(this, Strings.DiagnosticsSectionLabel, message, Strings.DiagnosticsShowInFolderButton))
            ExplorerLauncher.ShowInFolder(localPath);
    }

    private async Task<string?> BrowseForFolderAsync(string title)
    {
        var provider = StorageProvider;
        if (provider is null) return null;

        var result = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        var folder = result.Count > 0 ? result[0] : null;
        return folder?.TryGetLocalPath();
    }

    private async Task<bool> PerformSaveAsync()
    {
        var result = await _viewModel.SaveAsync();
        switch (result)
        {
            case SettingsSaveResult.ValidationFailed:
                SelectPage(SettingsPage.Folders); // both checks are about the folders: show what failed
                return false;
            case SettingsSaveResult.PersistFailed:
                await InfoDialog.ShowAsync(this, Strings.SettingsSaveFailedTitle, Strings.SettingsSaveFailedNotice);
                return false;
            case SettingsSaveResult.SavedLanguageChanged:
                // The texts the view model made (the lists of the combo boxes, the status lines), then the dialog's own
                _viewModel.RefreshLocalizedTexts();
                ApplyLocalizedTexts();
                UpdateHintDisplay();
                return true;
            default:
                return true;
        }
    }

    private Task<bool> SaveOnceAsync()
    {
        if (_saveInProgress is { IsCompleted: false } running) return running;
        return _saveInProgress = PerformSaveAsync();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e) => _ = _tasks.Run(SaveOnceAsync, "save settings");

    private void ExitButton_Click(object? sender, RoutedEventArgs e) => _ = RunCloseSafelyAsync();

    private void RequestClose(object? sender, RoutedEventArgs e) => _ = RunCloseSafelyAsync();

    private async Task RunCloseSafelyAsync()
    {
        try
        {
            await TryCloseAsync();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unhandled exception while closing settings dialog", ex);
        }
    }

    private async Task TryCloseAsync()
    {
        if (_isClosing) return;
        _isClosing = true;
        try
        {
            await _tasks.WaitAllAsync(Timeout.InfiniteTimeSpan);

            if (!_viewModel.IsDirty)
            {
                _forceClose = true;
                Close();
                return;
            }

            var choice = await UnsavedChangesDialog.AskAsync(this, Strings.UnsavedChangesTitle, Strings.UnsavedChangesMessage);
            switch (choice)
            {
                case UnsavedChangesChoice.Back:
                    return;
                case UnsavedChangesChoice.SaveAndExit:
                    if (!await SaveOnceAsync()) return;
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    break;
                case UnsavedChangesChoice.ExitWithoutSaving:
                    break;
                default:
                    return;
            }

            _forceClose = true;
            Close();
        }
        finally
        {
            _isClosing = false;
        }
    }

    private void SettingsDialog_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose) return;

        e.Cancel = true;
        _ = RunCloseSafelyAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }
}
