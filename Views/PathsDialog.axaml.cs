using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal partial class PathsDialog : Window
{
    private readonly AppSettingsData _settings;
    private string _savedGamePath;
    private string _savedSavePath;
    public bool SavePathChanged { get; private set; }

    public PathsDialog(AppSettingsData settings)
    {
        InitializeComponent();
        _settings = settings;

        Title = Strings.SettingsMenuPaths;
        TitleBarText.Text = Strings.SettingsMenuPaths;
        TitleText.Text = Strings.SettingsMenuPaths;
        GamePathLabel.Text = Strings.SettingsMenuGamePath;
        SavePathLabel.Text = Strings.SavePathFieldLabel;
        SaveButton.Content = Strings.ButtonSave;
        CloseDialogButton.Content = Strings.ButtonClose;

        _savedGamePath = _settings.GameInstallPath ?? "";
        _savedSavePath = _settings.SavePath ?? "";
        GamePathBox.Text = _savedGamePath;
        SavePathBox.Text = _savedSavePath;

        ShowNormalHint();
        UpdateSaveButtonState();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void PathBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ShowNormalHint();
        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        bool gameChanged = (GamePathBox.Text ?? "") != _savedGamePath;
        bool saveChanged = (SavePathBox.Text ?? "") != _savedSavePath;
        SaveButton.IsEnabled = gameChanged || saveChanged;
    }

    private void ShowNormalHint()
    {
        HintText.Text = Strings.PathFieldAutoHint + "\n" + Strings.SaveFolderContentHint;
        HintText.Foreground = GetBrush("TextSecondaryBrush");
    }

    private void ShowValidationError()
    {
        HintText.Text = Strings.SaveFolderValidationFailed;
        HintText.Foreground = GetBrush("DangerBrush");
    }

    private IBrush GetBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var res) == true && res is IBrush brush)
            return brush;
        return Brushes.Gray;
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

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        string gameValue = GamePathBox.Text?.Trim() ?? "";
        string saveValue = SavePathBox.Text?.Trim() ?? "";

        bool saveValueChanged = saveValue != _savedSavePath;
        if (saveValueChanged && !string.IsNullOrEmpty(saveValue) && !LocalSaveService.IsSavePathValid(saveValue))
        {
            ShowValidationError();
            return;
        }

        SavePathChanged = saveValueChanged;

        _settings.GameInstallPath = string.IsNullOrEmpty(gameValue) ? null : gameValue;
        _settings.SavePath = string.IsNullOrEmpty(saveValue) ? null : saveValue;
        AppSettingsService.Save(_settings);

        _savedGamePath = gameValue;
        _savedSavePath = saveValue;
        UpdateSaveButtonState();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
