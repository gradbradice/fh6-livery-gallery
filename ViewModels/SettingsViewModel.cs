using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.LiveryRender.Assets;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
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
    private bool _savedSearchByFolderName;
    private int _savedPossibleDuplicateThreshold;
    private bool _savedServerConnectionEnabled;
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
        _savedSearchByFolderName = settings.SearchByFolderName;
        _savedPossibleDuplicateThreshold = AppSettingsData.ClampPossibleDuplicateThreshold(settings.PossibleDuplicateThresholdPercent);
        _savedServerConnectionEnabled = settings.ServerConnectionEnabled;

        _themeMode = _savedThemeMode;
        _language = _savedLanguage;
        _gamePath = _savedGamePath;
        _savePath = _savedSavePath;
        _autoRefreshLiveries = _savedAutoRefreshLiveries;
        _refreshOnButtonClick = _savedRefreshOnButtonClick;
        _searchByFolderName = _savedSearchByFolderName;
        _possibleDuplicateThreshold = _savedPossibleDuplicateThreshold;
        _serverConnectionEnabled = _savedServerConnectionEnabled;

        _gameFolderCheckTask = CheckGameFolderAsync(_gamePath);
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
    private string _gameFolderStatusText = "";

    [ObservableProperty]
    private bool _gameFolderStatusIsError;

    private int _gameFolderCheckVersion;
    private Task _gameFolderCheckTask = Task.CompletedTask;
    private GameFolderCheck? _gameFolderCheck;

    partial void OnGamePathChanged(string value) => _gameFolderCheckTask = CheckGameFolderAsync(value);

    private async Task CheckGameFolderAsync(string path)
    {
        int version = ++_gameFolderCheckVersion;
        if (string.IsNullOrWhiteSpace(path))
        {
            _gameFolderCheck = null;
            GameFolderStatusText = Strings.GameFolderNotSetHint;
            GameFolderStatusIsError = false;
            return;
        }

        if (_gameFolderCheck is { GameDirectory: { } known } cached
            && string.Equals(Path.TrimEndingDirectorySeparator(known), Path.TrimEndingDirectorySeparator(path.Trim()), StringComparison.OrdinalIgnoreCase))
        {
            ApplyGameFolderCheck(cached);
            return;
        }

        GameFolderStatusText = Strings.GameFolderChecking;
        GameFolderStatusIsError = false;

        GameFolderCheck check;
        try
        {
            check = await Task.Run(() => GameFolder.Check(path));
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Game folder check failed for '{path}'", ex);
            check = new GameFolderCheck
            {
                InputPath = path,
                Issues = [new GameFolderIssue(GameFolderProblem.NotFound, true, ex.Message, path)],
            };
        }
        if (version != _gameFolderCheckVersion) return;

        _gameFolderCheck = check;
        if (check.IsValid && check.WasCorrected && check.GameDirectory is { } root)
        {
            GamePath = root;
            return;
        }
        ApplyGameFolderCheck(check);
    }

    private void ApplyGameFolderCheck(GameFolderCheck check)
    {
        if (!check.IsValid)
        {
            GameFolderStatusText = string.Format(Strings.GameFolderInvalidFormat,
                GameFolderText.FirstError(check) ?? Strings.GameFolderProblemUnknown);
            GameFolderStatusIsError = true;
            return;
        }

        string warnings = GameFolderText.Warnings(check);
        GameFolderStatusText = warnings.Length == 0
            ? string.Format(Strings.GameFolderValidFormat, check.CarArchiveCount)
            : string.Format(Strings.GameFolderValidWithWarningsFormat, check.CarArchiveCount, warnings);
        GameFolderStatusIsError = false;
    }

    private async Task<bool> IsGameFolderAcceptableAsync()
    {
        if (GamePath == _savedGamePath || string.IsNullOrWhiteSpace(GamePath)) return true;

        Task pending;
        do
        {
            pending = _gameFolderCheckTask;
            await pending;
        }
        while (!ReferenceEquals(pending, _gameFolderCheckTask));

        return _gameFolderCheck is { IsValid: true };
    }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _searchByFolderName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _serverConnectionEnabled;

    public bool IsServerConnectionSaved => _savedServerConnectionEnabled;

    public double MinPossibleDuplicateThreshold => AppSettingsData.MinPossibleDuplicateThresholdPercent;
    public double MaxPossibleDuplicateThreshold => AppSettingsData.MaxPossibleDuplicateThresholdPercent;

    private int _possibleDuplicateThreshold;

    public double PossibleDuplicateThreshold
    {
        get => _possibleDuplicateThreshold;
        set
        {
            int percent = AppSettingsData.ClampPossibleDuplicateThreshold((int)Math.Round(value));
            if (percent == _possibleDuplicateThreshold) return;
            _possibleDuplicateThreshold = percent;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PossibleDuplicateThresholdText));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string PossibleDuplicateThresholdText => $"{_possibleDuplicateThreshold} %";

    public bool IsDirty =>
        ThemeMode != _savedThemeMode
        || Language != _savedLanguage
        || GamePath != _savedGamePath
        || SavePath != _savedSavePath
        || AutoRefreshLiveries != _savedAutoRefreshLiveries
        || RefreshOnButtonClick != _savedRefreshOnButtonClick
        || SearchByFolderName != _savedSearchByFolderName
        || _possibleDuplicateThreshold != _savedPossibleDuplicateThreshold
        || ServerConnectionEnabled != _savedServerConnectionEnabled;

    public async Task<SettingsSaveResult> SaveAsync()
    {
        if (!await IsGameFolderAcceptableAsync()) return SettingsSaveResult.ValidationFailed;

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
        candidate.SearchByFolderName = SearchByFolderName;
        candidate.PossibleDuplicateThresholdPercent = _possibleDuplicateThreshold;
        candidate.ServerConnectionEnabled = ServerConnectionEnabled;

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
        _savedSearchByFolderName = SearchByFolderName;
        _savedPossibleDuplicateThreshold = _possibleDuplicateThreshold;
        _savedServerConnectionEnabled = ServerConnectionEnabled;
        OnPropertyChanged(nameof(IsDirty));
    }
}
