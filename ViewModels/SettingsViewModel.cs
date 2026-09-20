using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Enums;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed record LanguageOption(AppLanguage Language, string DisplayName);

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsData _settings;
    private readonly bool _saveInitialWasAutoDiscovered;
    private readonly string _initialSavePath;
    private AppThemeMode _savedThemeMode;
    private AppLanguage _savedLanguage;
    private string _savedGamePath;
    private string _savedSavePath;
    private bool _savedAutoRefreshLiveries;
    private bool _savedRefreshOnButtonClick;
    public bool SavePathChanged { get; private set; }

    public SettingsViewModel(AppSettingsData settings, string? resolvedSavePath)
    {
        _settings = settings;

        _savedThemeMode = settings.ThemeMode ?? AppThemeMode.System;
        _savedLanguage = settings.Language;
        _saveInitialWasAutoDiscovered = string.IsNullOrEmpty(settings.SavePath);
        _savedGamePath = settings.GameInstallPath ?? "";
        _savedSavePath = settings.SavePath ?? resolvedSavePath ?? "";
        _initialSavePath = _savedSavePath;
        _savedAutoRefreshLiveries = settings.AutoRefreshLiveries;
        _savedRefreshOnButtonClick = settings.RefreshLiveriesOnButtonClick;

        _themeMode = _savedThemeMode;
        _language = _savedLanguage;
        _gamePath = _savedGamePath;
        _savePath = _savedSavePath;
        _autoRefreshLiveries = _savedAutoRefreshLiveries;
        _refreshOnButtonClick = _savedRefreshOnButtonClick;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private AppThemeMode _themeMode;

    public bool IsSystemTheme => ThemeMode == AppThemeMode.System;
    public bool IsLightTheme => ThemeMode == AppThemeMode.Light;
    public bool IsDarkTheme => ThemeMode == AppThemeMode.Dark;

    [RelayCommand]
    private void SetTheme(AppThemeMode mode) => ThemeMode = mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLanguageOption))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private AppLanguage _language;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(AppLanguage.German, "Deutsch (DE)"),
        new(AppLanguage.English, "English (EN)"),
        new(AppLanguage.Spanish, "Español (ES)"),
        new(AppLanguage.French, "Français (FR)"),
        new(AppLanguage.Italian, "Italiano (IT)"),
        new(AppLanguage.Japanese, "日本語 (JA)"),
        new(AppLanguage.Korean, "한국어 (KO)"),
        new(AppLanguage.Portuguese, "Português (PT)"),
        new(AppLanguage.Russian, "Русский (RU)"),
        new(AppLanguage.ChineseSimplified, "中文（简体） (ZH-CN)"),
        new(AppLanguage.ChineseTraditional, "中文（繁體） (ZH-TW)"),
    ];

    public LanguageOption SelectedLanguageOption
    {
        get => LanguageOptions.First(o => o.Language == Language);
        set => Language = value.Language;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _gamePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(HintIsError))]
    private string _savePath = "";
    public bool HintIsError { get; private set; }

    partial void OnSavePathChanged(string value) => HintIsError = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _autoRefreshLiveries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _refreshOnButtonClick;

    public bool IsDirty =>
        ThemeMode != _savedThemeMode
        || Language != _savedLanguage
        || GamePath != _savedGamePath
        || SavePath != _savedSavePath
        || AutoRefreshLiveries != _savedAutoRefreshLiveries
        || RefreshOnButtonClick != _savedRefreshOnButtonClick;

    public async Task<SettingsSaveResult> SaveAsync()
    {
        bool saveValueChanged = SavePath != _savedSavePath;
        if (saveValueChanged && !string.IsNullOrEmpty(SavePath) && !LocalSaveService.IsSavePathValid(SavePath))
        {
            HintIsError = true;
            OnPropertyChanged(nameof(HintIsError));
            return SettingsSaveResult.ValidationFailed;
        }

        var candidate = _settings.Clone();
        candidate.ThemeMode = ThemeMode;
        candidate.Language = Language;
        candidate.GameInstallPath = string.IsNullOrEmpty(GamePath) ? null : GamePath;
        candidate.SavePath = SavePath == _savedSavePath && _saveInitialWasAutoDiscovered
            ? null
            : (string.IsNullOrEmpty(SavePath) ? null : SavePath);
        candidate.AutoRefreshLiveries = AutoRefreshLiveries;
        candidate.RefreshLiveriesOnButtonClick = RefreshOnButtonClick;

        bool saved = await AppSettingsService.SaveImmediateAsync(candidate);
        if (!saved) return SettingsSaveResult.PersistFailed;

        bool languageChanged = Language != AppLocalisationService.AppLanguage;
        _settings.CopyFrom(candidate);
        AppThemeService.ApplyTheme(ThemeMode);
        if (languageChanged) AppLocalisationService.AppLanguage = Language;

        if (SavePath != _initialSavePath) SavePathChanged = true;

        CommitSaved();
        return languageChanged ? SettingsSaveResult.SavedLanguageChanged : SettingsSaveResult.Saved;
    }

    private void CommitSaved()
    {
        _savedThemeMode = ThemeMode;
        _savedLanguage = Language;
        _savedGamePath = GamePath;
        _savedSavePath = SavePath;
        _savedAutoRefreshLiveries = AutoRefreshLiveries;
        _savedRefreshOnButtonClick = RefreshOnButtonClick;
        OnPropertyChanged(nameof(IsDirty));
    }
}
