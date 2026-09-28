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
using LiveryGallery.ViewModels;

namespace LiveryGallery.Views;

internal partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly BackgroundTaskTracker _tasks = new();
    private Task<bool>? _saveInProgress;
    private bool _forceClose;
    private bool _isClosing;
    private bool _closed;

    public bool SavePathChanged => _viewModel.SavePathChanged;

    public SettingsDialog(AppSettingsData settings, string? resolvedSavePath)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(settings, resolvedSavePath);
        DataContext = _viewModel;

        ApplyLocalizedTexts();
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
            string? discovered = await GameDiscoveryService.TryFindGamePathAsync();
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
        PathsSectionLabel.Text = Strings.SettingsMenuPaths;
        GamePathLabel.Text = Strings.SettingsMenuGamePath;
        SavePathLabel.Text = Strings.SavePathFieldLabel;
        ScanningSectionLabel.Text = Strings.SettingsSectionScanning;
        AutoRefreshLiveriesCheckBox.Content = Strings.AutoRefreshLiveriesLabel;
        RefreshOnButtonClickCheckBox.Content = Strings.RefreshOnButtonClickLabel;
        SearchByFolderNameCheckBox.Content = Strings.SearchByFolderNameLabel;
        ServerSectionLabel.Text = Strings.SettingsSectionServer;
        ServerConnectionCheckBox.Content = Strings.ServerConnectionLabel;
        ServerConnectionDescription.Text = Strings.ServerConnectionDescription;
        DuplicatesSectionLabel.Text = Strings.SettingsSectionDuplicates;
        PossibleDuplicateThresholdLabel.Text = Strings.PossibleDuplicateThresholdLabel;
        PossibleDuplicateThresholdHint.Text = Strings.PossibleDuplicateThresholdHint;
        SystemThemeRadio.Content = Strings.ThemeSystemLabel;
        LightThemeRadio.Content = Strings.ThemeLightLabel;
        DarkThemeRadio.Content = Strings.ThemeDarkLabel;
        ExitButton.Content = Strings.ButtonExit;
        SaveButton.Content = Strings.ButtonSave;
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
            HintText.Text = Strings.PathFieldAutoHint + ". " + Strings.SaveFolderContentHint;
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
                return false;
            case SettingsSaveResult.PersistFailed:
                await InfoDialog.ShowAsync(this, Strings.SettingsSaveFailedTitle, Strings.SettingsSaveFailedNotice);
                return false;
            case SettingsSaveResult.SavedLanguageChanged:
                ApplyLocalizedTexts();
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
