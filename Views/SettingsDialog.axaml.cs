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
    private bool _forceClose;
    private bool _isClosing;

    public bool SavePathChanged => _viewModel.SavePathChanged;
    public bool LanguageChanged { get; private set; }

    public SettingsDialog(AppSettingsData settings, string? resolvedSavePath)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(settings, resolvedSavePath);
        DataContext = _viewModel;

        ApplyLocalizedTexts();
        UpdateHintDisplay();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.HintIsError)) UpdateHintDisplay();
        };

        Closing += SettingsDialog_Closing;
        if (string.IsNullOrEmpty(_viewModel.GamePath))
            _ = PopulateDiscoveredGamePathAsync();
    }

    private async Task PopulateDiscoveredGamePathAsync()
    {
        string? discovered = await GameDiscoveryService.TryFindGamePathAsync();
        if (discovered is null) return;
        if (!string.IsNullOrEmpty(_viewModel.GamePath)) return;

        _viewModel.GamePath = discovered;
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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private async void GameBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await BrowseForFolderAsync(Strings.SelectGameFolderDialogTitle);
        if (path is not null) _viewModel.GamePath = path;
    }

    private async void SaveBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await BrowseForFolderAsync(Strings.SelectFolderDialogTitle);
        if (path is not null) _viewModel.SavePath = path;
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
                return false;
            case SettingsSaveResult.PersistFailed:
                await InfoDialog.ShowAsync(this, Strings.SettingsSaveFailedTitle, Strings.SettingsSaveFailedNotice);
                return false;
            case SettingsSaveResult.SavedLanguageChanged:
                ApplyLocalizedTexts();
                LanguageChanged = true;
                return true;
            default:
                return true;
        }
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e) => await PerformSaveAsync();

    private async void ExitButton_Click(object? sender, RoutedEventArgs e) => await TryCloseAsync();

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
                    if (!await PerformSaveAsync()) return;
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
}
