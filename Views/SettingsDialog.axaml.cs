using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class SettingsDialog : Window
{
    private readonly AppSettingsData _settings;
    private AppThemeMode _savedThemeMode;
    private AppLanguage _savedLanguage;
    private string _savedGamePath;
    private string _savedSavePath;
    private readonly string _initialSavePath;
    private bool _forceClose;

    public bool SavePathChanged { get; private set; }

    public SettingsDialog(AppSettingsData settings)
    {
        InitializeComponent();
        _settings = settings;

        ApplyLocalizedTexts();

        _savedThemeMode = _settings.ThemeMode ?? AppThemeMode.System;
        _savedLanguage = _settings.Language;

        SelectThemeRadio(_savedThemeMode);
        SelectLanguageInCombo(_savedLanguage);

        string? gameInitial = _settings.GameInstallPath ?? GameDiscoveryService.TryFindGamePath();
        string? saveInitial = _settings.SavePath ?? "";
        _savedGamePath = gameInitial ?? "";
        _savedSavePath = saveInitial;
        _initialSavePath = saveInitial;
        GamePathBox.Text = _savedGamePath;
        SavePathBox.Text = _savedSavePath;

        UpdateSaveButtonState();

        Closing += SettingsDialog_Closing;
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
        SystemThemeRadio.Content = Strings.ThemeSystemLabel;
        LightThemeRadio.Content = Strings.ThemeLightLabel;
        DarkThemeRadio.Content = Strings.ThemeDarkLabel;
        ExitButton.Content = Strings.ButtonExit;
        SaveButton.Content = Strings.ButtonSave;
        ShowNormalHint();
    }

    private void SelectThemeRadio(AppThemeMode mode)
    {
        SystemThemeRadio.IsChecked = mode == AppThemeMode.System;
        LightThemeRadio.IsChecked = mode == AppThemeMode.Light;
        DarkThemeRadio.IsChecked = mode == AppThemeMode.Dark;
    }

    private AppThemeMode GetSelectedThemeMode()
    {
        if (LightThemeRadio.IsChecked == true) return AppThemeMode.Light;
        if (DarkThemeRadio.IsChecked == true) return AppThemeMode.Dark;
        return AppThemeMode.System;
    }

    private void SelectLanguageInCombo(AppLanguage language)
    {
        string code = AppLocalisationService.AppLanguageToString(language);

        foreach (var obj in LanguageCombo.Items)
        {
            if (obj is ComboBoxItem { Tag: string tag } item && tag == code)
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }
    }

    private AppLanguage GetSelectedLanguage()
    {
        if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string code }) return _savedLanguage;
        return AppLocalisationService.StringToAppLanguage(code);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => this.HandleTitleBarDrag(e);

    private void ThemeOption_Click(object? sender, RoutedEventArgs e) => UpdateSaveButtonState();

    private void LanguageCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSaveButtonState();

    private void PathBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ShowNormalHint();
        UpdateSaveButtonState();
    }

    private bool IsDirty()
    {
        bool themeChanged = GetSelectedThemeMode() != _savedThemeMode;
        bool languageChanged = GetSelectedLanguage() != _savedLanguage;
        bool gameChanged = (GamePathBox.Text ?? "") != _savedGamePath;
        bool saveChanged = (SavePathBox.Text ?? "") != _savedSavePath;
        return themeChanged || languageChanged || gameChanged || saveChanged;
    }

    private void UpdateSaveButtonState() => SaveButton.IsEnabled = IsDirty();

    private void ShowNormalHint()
    {
        HintText.Text = Strings.PathFieldAutoHint + ". " + Strings.SaveFolderContentHint;
        HintText.Foreground = this.GetThemeBrush("TextSecondaryBrush");
    }

    private void ShowValidationError()
    {
        HintText.Text = Strings.SaveFolderValidationFailed;
        HintText.Foreground = this.GetThemeBrush("DangerBrush");
    }


    private async void GameBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await BrowseForFolderAsync(Strings.SelectGameFolderDialogTitle);
        if (path is not null) GamePathBox.Text = path;
    }

    private async void SaveBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await BrowseForFolderAsync(Strings.SelectFolderDialogTitle);
        if (path is not null) SavePathBox.Text = path;
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

    private bool PerformSave()
    {
        AppThemeMode themeMode = GetSelectedThemeMode();
        AppLanguage languageValue = GetSelectedLanguage();
        string gameValue = GamePathBox.Text?.Trim() ?? "";
        string saveValue = SavePathBox.Text?.Trim() ?? "";

        bool saveValueChanged = saveValue != _savedSavePath;
        if (saveValueChanged && !string.IsNullOrEmpty(saveValue) && !LocalSaveService.IsSavePathValid(saveValue))
        {
            ShowValidationError();
            return false;
        }

        AppThemeService.ApplyTheme(themeMode, persist: false);
        if (languageValue != AppLocalisationService.AppLanguage)
        {
            AppLocalisationService.AppLanguage = languageValue;
            ApplyLocalizedTexts();
            (Owner as MainWindow)?.OnLanguageChanged();
        }

        _settings.ThemeMode = themeMode;
        _settings.Language = languageValue;
        _settings.GameInstallPath = string.IsNullOrEmpty(gameValue) ? null : gameValue;
        _settings.SavePath = string.IsNullOrEmpty(saveValue) ? null : saveValue;
        AppSettingsService.SaveImmediate(_settings);

        if (saveValue != _initialSavePath) SavePathChanged = true;

        _savedThemeMode = themeMode;
        _savedLanguage = languageValue;
        _savedGamePath = gameValue;
        _savedSavePath = saveValue;
        UpdateSaveButtonState();
        return true;
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e) => PerformSave();

    private async void ExitButton_Click(object? sender, RoutedEventArgs e) => await TryCloseAsync();

    private void RequestClose(object? sender, RoutedEventArgs e) => _ = TryCloseAsync();

    private async Task TryCloseAsync()
    {
        if (!IsDirty())
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
                if (!PerformSave()) return;
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

    private void SettingsDialog_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose) return;

        e.Cancel = true;
        _ = TryCloseAsync();
    }
}
