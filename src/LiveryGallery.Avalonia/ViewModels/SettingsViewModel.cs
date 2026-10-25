using ForzaToolkit.Games.Installation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.StringTables;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Viewer3D;
using System.Collections.ObjectModel;
using System.Globalization;
// The view model has a PossibleDuplicateThreshold property of its own; the type is every app's range and default
using DuplicateThreshold = ForzaToolkit.Liveries.PossibleDuplicateThreshold;

namespace LiveryGallery.ViewModels;

internal sealed record LanguageOption(AppLanguage Language, string DisplayName);

/// <summary>
/// A choice of a combo box whose name is in the interface language. The objects stay for the life of the dialog and
/// only their name changes with the language: a combo box keeps its selection, which it loses when its items are
/// replaced by new ones.
/// </summary>
internal abstract partial class LocalizedOption(string displayName) : ObservableObject
{
    [ObservableProperty]
    private string _displayName = displayName;
}

internal sealed class RowsModeOption(MyDesignsRowsMode mode, string displayName) : LocalizedOption(displayName)
{
    public MyDesignsRowsMode Mode { get; } = mode;
}

/// <summary>A language of car names: a game language code, or null for "as in the interface"</summary>
internal sealed class CarTextLanguageOption(string? code, string displayName) : LocalizedOption(displayName)
{
    public string? Code { get; } = code;
}

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsData _settings;
    private readonly GameProfile _profile;
    private readonly bool _saveInitialWasAutoDiscovered;
    private readonly string _initialSavePath;
    private AppThemeMode _savedThemeMode;
    private AppLanguage _savedLanguage;
    private string? _savedCarTextLanguage;
    private string _savedGamePath;
    private string _savedSavePath;
    private bool _savedAutoRefreshLiveries;
    private bool _savedRefreshOnButtonClick;
    private bool _savedSearchByFolderName;
    private int _savedPossibleDuplicateThreshold;
    private bool _savedServerConnectionEnabled;
    private MyDesignsRowsMode _savedRowsMode;
    private int _savedKeyInterval;
    private bool _savedShowChangesSinceLastLaunch;
    private bool _savedWarnFewFreeSlots;
    private bool _savedAutoBackupBeforeArchive;
    private bool _savedAutoBackupOnChange;
    private bool _savedAutoBackupPeriodic;
    private int _savedAutoBackupPeriodDays;
    private int _savedAutoBackupKeep;
    public bool SavePathChanged { get; private set; }

    /// <param name="carTextLanguages">The game languages whose texts were read from the game; empty before the first read</param>
    public SettingsViewModel(
        AppSettingsData settings, string? resolvedSavePath, IGameFolderCheck gameFolderCheck, GameProfile profile,
        IReadOnlyList<GameLanguage>? carTextLanguages = null)
    {
        _settings = settings;
        _profile = profile;
        _gameFolderChecker = gameFolderCheck;

        _savedThemeMode = settings.ThemeMode ?? AppThemeMode.System;
        _savedLanguage = settings.Language;
        _savedCarTextLanguage = string.IsNullOrEmpty(settings.CarTextLanguage) ? null : settings.CarTextLanguage;
        _saveInitialWasAutoDiscovered = string.IsNullOrEmpty(settings.SavePath);
        _savedGamePath = settings.GameInstallPath ?? "";
        _savedSavePath = settings.SavePath ?? resolvedSavePath ?? "";
        _initialSavePath = _savedSavePath;
        _savedAutoRefreshLiveries = settings.AutoRefreshLiveries;
        _savedRefreshOnButtonClick = settings.RefreshLiveriesOnButtonClick;
        _savedSearchByFolderName = settings.SearchByFolderName;
        _savedPossibleDuplicateThreshold = DuplicateThreshold.Clamp(settings.PossibleDuplicateThresholdPercent);
        _savedServerConnectionEnabled = settings.ServerConnectionEnabled;
        _savedRowsMode = settings.MyDesignsRowsMode;
        _savedKeyInterval = NavigationTiming.ClampKeyInterval(settings.NavigationKeyIntervalMs);
        _savedShowChangesSinceLastLaunch = settings.ShowChangesSinceLastLaunch;
        _savedWarnFewFreeSlots = settings.WarnFewFreeSlots;
        _savedAutoBackupBeforeArchive = settings.AutoBackupBeforeArchive;
        _savedAutoBackupOnChange = settings.AutoBackupOnChange;
        _savedAutoBackupPeriodic = settings.AutoBackupPeriodic;
        _savedAutoBackupPeriodDays = AutoBackupPolicy.ClampPeriodDays(settings.AutoBackupPeriodDays);
        _savedAutoBackupKeep = AutoBackupPolicy.ClampKeep(settings.AutoBackupKeep);

        _themeMode = _savedThemeMode;
        _language = _savedLanguage;
        _carTextLanguage = _savedCarTextLanguage;
        _gamePath = _savedGamePath;
        _savePath = _savedSavePath;
        _autoRefreshLiveries = _savedAutoRefreshLiveries;
        _refreshOnButtonClick = _savedRefreshOnButtonClick;
        _searchByFolderName = _savedSearchByFolderName;
        _possibleDuplicateThreshold = _savedPossibleDuplicateThreshold;
        _serverConnectionEnabled = _savedServerConnectionEnabled;
        _rowsMode = _savedRowsMode;
        _keyInterval = _savedKeyInterval;
        _showChangesSinceLastLaunch = _savedShowChangesSinceLastLaunch;
        _warnFewFreeSlots = _savedWarnFewFreeSlots;
        _autoBackupBeforeArchive = _savedAutoBackupBeforeArchive;
        _autoBackupOnChange = _savedAutoBackupOnChange;
        _autoBackupPeriodic = _savedAutoBackupPeriodic;
        _autoBackupPeriodDays = _savedAutoBackupPeriodDays;
        _autoBackupKeep = _savedAutoBackupKeep;

        _carTextLanguages = carTextLanguages ?? [];
        HasGameCarTexts = _carTextLanguages.Count > 0;
        CarTextLanguageOptions = BuildCarTextLanguageOptions();
        RowsModeOptions =
        [
            new(MyDesignsRowsMode.Auto, RowsModeName(MyDesignsRowsMode.Auto)),
            new(MyDesignsRowsMode.Two, RowsModeName(MyDesignsRowsMode.Two)),
            new(MyDesignsRowsMode.Three, RowsModeName(MyDesignsRowsMode.Three)),
        ];

        _gameFolderCheckTask = CheckGameFolderAsync(_gamePath);
    }

    /// <summary>
    /// Names of languages in alphabetical order, the same whatever the interface language: Latin letters first
    /// (Č with C), then the other scripts. Case does not count.
    /// </summary>
    private static readonly StringComparer LanguageNameOrder =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

    /// <summary>"As in the interface" (in the current interface language) always first, then the languages of the game
    /// by their names</summary>
    private List<CarTextLanguageOption> BuildCarTextLanguageOptions()
    {
        var languages = _carTextLanguages.Select(l => new CarTextLanguageOption(l.Code, $"{l.NativeName} ({l.Code})")).ToList();
        // A chosen language whose archive is not read (yet): kept, and shown so that saving does not drop it
        foreach (string? code in new[] { _savedCarTextLanguage, CarTextLanguage })
        {
            if (code is { } chosen && !languages.Any(o => string.Equals(o.Code, chosen, StringComparison.OrdinalIgnoreCase)))
                languages.Add(new(chosen, $"{GameLanguages.Get(chosen).NativeName} ({chosen})"));
        }
        return
        [
            new(null, Strings.CarTextLanguageAsInterface),
            .. languages.OrderBy(o => o.DisplayName, LanguageNameOrder),
        ];
    }

    private static string RowsModeName(MyDesignsRowsMode mode) => mode switch
    {
        MyDesignsRowsMode.Two => Strings.NavigationRowsTwo,
        MyDesignsRowsMode.Three => Strings.NavigationRowsThree,
        _ => Strings.NavigationRowsAuto,
    };

    /// <summary>
    /// Builds again every text the view model made in the interface language: after the language is changed (and saved)
    /// with the dialog open, the lists and the status lines follow it like the rest of the dialog. The choices stay.
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        // Only the names change; the options are the same objects, so the combo boxes keep what is selected
        CarTextLanguageOptions[0].DisplayName = Strings.CarTextLanguageAsInterface;
        foreach (var option in RowsModeOptions) option.DisplayName = RowsModeName(option.Mode);

        if (_shownGameFolderCheck is { } shown) ApplyGameFolderCheck(shown); // also the rows status
        else
        {
            GameFolderStatusText = string.IsNullOrWhiteSpace(GamePath) ? Strings.GameFolderNotSetHint : Strings.GameFolderChecking;
            UpdateRowsStatus();
        }

        OnPropertyChanged(nameof(KeyIntervalText));
        OnPropertyChanged(nameof(AutoBackupPeriodText));
        OnPropertyChanged(nameof(AutoBackupKeepText));
        SetStoredAccounts([.. StoredAccounts.Select(r => r.Account)]); // their status lines, without reading the disk again
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

    /// <summary>The interface languages by their names, in the same order as the languages of car names</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
        [.. AppLanguages.InMenuOrder.Select(l => new LanguageOption(l, AppLanguages.DisplayName(l)))];

    public LanguageOption SelectedLanguageOption
    {
        get => LanguageOptions.First(o => o.Language == Language);
        set => Language = value.Language;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCarTextLanguageOption))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string? _carTextLanguage;

    private readonly IReadOnlyList<GameLanguage> _carTextLanguages;

    /// <summary>"As in the interface", then the languages of the game</summary>
    public IReadOnlyList<CarTextLanguageOption> CarTextLanguageOptions { get; }

    /// <summary>Texts were read from the game; otherwise car names are in English whatever is chosen</summary>
    public bool HasGameCarTexts { get; }

    public CarTextLanguageOption SelectedCarTextLanguageOption
    {
        get => CarTextLanguageOptions.FirstOrDefault(o => string.Equals(o.Code, CarTextLanguage, StringComparison.OrdinalIgnoreCase))
            ?? CarTextLanguageOptions[0];
        set
        {
            // Null is not "as in the interface" (that option has a null code): it is the combo box clearing its
            // selection while its items are replaced, and must not change the choice
            if (value is not null) CarTextLanguage = value.Code;
        }
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
    private readonly IGameFolderCheck _gameFolderChecker;
    private GameFolderReport? _gameFolderCheck;
    /// <summary>The check the status line shows; null while it shows "not set" or "checking"</summary>
    private GameFolderReport? _shownGameFolderCheck;

    partial void OnGamePathChanged(string value) => _gameFolderCheckTask = CheckGameFolderAsync(value);

    private async Task CheckGameFolderAsync(string path)
    {
        int version = ++_gameFolderCheckVersion;
        if (string.IsNullOrWhiteSpace(path))
        {
            _gameFolderCheck = null;
            _shownGameFolderCheck = null;
            GameFolderStatusText = Strings.GameFolderNotSetHint;
            GameFolderStatusIsError = false;
            UpdateRowsStatus();
            return;
        }

        if (_gameFolderCheck is { GameDirectory: { } known } cached
            && string.Equals(Path.TrimEndingDirectorySeparator(known), Path.TrimEndingDirectorySeparator(path.Trim()), StringComparison.OrdinalIgnoreCase))
        {
            ApplyGameFolderCheck(cached);
            return;
        }

        _shownGameFolderCheck = null;
        GameFolderStatusText = Strings.GameFolderChecking;
        GameFolderStatusIsError = false;

        var check = await _gameFolderChecker.CheckAsync(path);
        if (version != _gameFolderCheckVersion) return;

        _gameFolderCheck = check;
        if (check.IsValid && check.WasCorrected && check.GameDirectory is { } root)
        {
            GamePath = root;
            return;
        }
        ApplyGameFolderCheck(check);
    }

    private void ApplyGameFolderCheck(GameFolderReport check)
    {
        _shownGameFolderCheck = check;
        UpdateRowsStatus();
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
    private bool _showChangesSinceLastLaunch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _warnFewFreeSlots;

    /// <summary>Filled by the dialog once it is open (the sizes take disk work); not part of the saved settings</summary>
    public ObservableCollection<StoredAccountRowViewModel> StoredAccounts { get; } = [];
    public bool HasStoredAccounts => StoredAccounts.Count > 0;

    public void SetStoredAccounts(IEnumerable<StoredAccount> accounts)
    {
        StoredAccounts.Clear();
        foreach (var account in accounts) StoredAccounts.Add(new StoredAccountRowViewModel(account));
        OnPropertyChanged(nameof(HasStoredAccounts));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _serverConnectionEnabled;

    public bool IsServerConnectionSaved => _savedServerConnectionEnabled;

    public double MinPossibleDuplicateThreshold => DuplicateThreshold.MinPercent;
    public double MaxPossibleDuplicateThreshold => DuplicateThreshold.MaxPercent;

    private int _possibleDuplicateThreshold;

    public double PossibleDuplicateThreshold
    {
        get => _possibleDuplicateThreshold;
        set
        {
            int percent = DuplicateThreshold.Clamp((int)Math.Round(value));
            if (percent == _possibleDuplicateThreshold) return;
            _possibleDuplicateThreshold = percent;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PossibleDuplicateThresholdText));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string PossibleDuplicateThresholdText => $"{_possibleDuplicateThreshold} %";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _autoBackupBeforeArchive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _autoBackupOnChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _autoBackupPeriodic;

    public double MinAutoBackupPeriodDays => AutoBackupPolicy.MinPeriodDays;
    public double MaxAutoBackupPeriodDays => AutoBackupPolicy.MaxPeriodDays;
    public double MinAutoBackupKeep => AutoBackupPolicy.MinKeep;
    public double MaxAutoBackupKeep => AutoBackupPolicy.MaxKeep;

    private int _autoBackupPeriodDays;

    public double AutoBackupPeriodDays
    {
        get => _autoBackupPeriodDays;
        set
        {
            int days = AutoBackupPolicy.ClampPeriodDays((int)Math.Round(value));
            if (days == _autoBackupPeriodDays) return;
            _autoBackupPeriodDays = days;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoBackupPeriodText));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string AutoBackupPeriodText => string.Format(Strings.AutoBackupDaysFormat, _autoBackupPeriodDays);

    private int _autoBackupKeep;

    public double AutoBackupKeep
    {
        get => _autoBackupKeep;
        set
        {
            int keep = AutoBackupPolicy.ClampKeep((int)Math.Round(value));
            if (keep == _autoBackupKeep) return;
            _autoBackupKeep = keep;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoBackupKeepText));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string AutoBackupKeepText => _autoBackupKeep.ToString(AppLocalisationService.Culture);

    public IReadOnlyList<RowsModeOption> RowsModeOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRowsModeOption))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private MyDesignsRowsMode _rowsMode;

    public RowsModeOption SelectedRowsModeOption
    {
        get => RowsModeOptions.FirstOrDefault(o => o.Mode == RowsMode) ?? RowsModeOptions[0];
        set
        {
            if (value is not null) RowsMode = value.Mode;
        }
    }

    partial void OnRowsModeChanged(MyDesignsRowsMode value) => UpdateRowsStatus();

    [ObservableProperty]
    private string _rowsStatusText = "";

    private void UpdateRowsStatus()
    {
        if (RowsMode != MyDesignsRowsMode.Auto)
        {
            RowsStatusText = "";
            return;
        }

        if (_profile.MyDesigns is not { } grid)
        {
            RowsStatusText = "";
            return;
        }
        string? gameDirectory = _gameFolderCheck is { IsValid: true, GameDirectory: { } root } ? root : null;
        var detection = grid.DetectRows(gameDirectory);
        RowsStatusText = detection.Source switch
        {
            GridRowsSource.Game => Strings.NavigationRowsDetectedVanilla,
            GridRowsSource.Mod => string.Format(Strings.NavigationRowsDetectedModFormat, detection.Rows),
            GridRowsSource.GameFolderUnknown => Strings.NavigationRowsDetectedNoGameFolder,
            _ => Strings.NavigationRowsDetectedUnknown,
        };
    }

    public double MinKeyInterval => NavigationTiming.MinKeyIntervalMs;
    public double MaxKeyInterval => NavigationTiming.MaxKeyIntervalMs;

    private int _keyInterval;

    public double KeyInterval
    {
        get => _keyInterval;
        set
        {
            int ms = NavigationTiming.ClampKeyInterval((int)Math.Round(value));
            if (ms == _keyInterval) return;
            _keyInterval = ms;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeyIntervalText));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public string KeyIntervalText => string.Format(Strings.MillisecondsFormat, _keyInterval);

    public bool IsDirty =>
        ThemeMode != _savedThemeMode
        || Language != _savedLanguage
        || !string.Equals(CarTextLanguage, _savedCarTextLanguage, StringComparison.OrdinalIgnoreCase)
        || GamePath != _savedGamePath
        || SavePath != _savedSavePath
        || AutoRefreshLiveries != _savedAutoRefreshLiveries
        || RefreshOnButtonClick != _savedRefreshOnButtonClick
        || SearchByFolderName != _savedSearchByFolderName
        || _possibleDuplicateThreshold != _savedPossibleDuplicateThreshold
        || ServerConnectionEnabled != _savedServerConnectionEnabled
        || RowsMode != _savedRowsMode
        || _keyInterval != _savedKeyInterval
        || ShowChangesSinceLastLaunch != _savedShowChangesSinceLastLaunch
        || WarnFewFreeSlots != _savedWarnFewFreeSlots
        || AutoBackupBeforeArchive != _savedAutoBackupBeforeArchive
        || AutoBackupOnChange != _savedAutoBackupOnChange
        || AutoBackupPeriodic != _savedAutoBackupPeriodic
        || _autoBackupPeriodDays != _savedAutoBackupPeriodDays
        || _autoBackupKeep != _savedAutoBackupKeep;

    public async Task<SettingsSaveResult> SaveAsync()
    {
        if (!await IsGameFolderAcceptableAsync()) return SettingsSaveResult.ValidationFailed;

        bool saveValueChanged = SavePath != _savedSavePath;
        if (saveValueChanged && !string.IsNullOrEmpty(SavePath) && !_profile.Saves.IsValidSaveFolder(SavePath))
        {
            HintIsError = true;
            OnPropertyChanged(nameof(HintIsError));
            return SettingsSaveResult.ValidationFailed;
        }

        var candidate = _settings.Clone();
        candidate.ThemeMode = ThemeMode;
        candidate.Language = Language;
        candidate.CarTextLanguage = CarTextLanguage;
        candidate.GameInstallPath = string.IsNullOrEmpty(GamePath) ? null : GamePath;
        candidate.SavePath = SavePath == _savedSavePath && _saveInitialWasAutoDiscovered
            ? null
            : (string.IsNullOrEmpty(SavePath) ? null : SavePath);
        candidate.AutoRefreshLiveries = AutoRefreshLiveries;
        candidate.RefreshLiveriesOnButtonClick = RefreshOnButtonClick;
        candidate.SearchByFolderName = SearchByFolderName;
        candidate.PossibleDuplicateThresholdPercent = _possibleDuplicateThreshold;
        candidate.ServerConnectionEnabled = ServerConnectionEnabled;
        candidate.MyDesignsRowsMode = RowsMode;
        candidate.NavigationKeyIntervalMs = _keyInterval;
        candidate.ShowChangesSinceLastLaunch = ShowChangesSinceLastLaunch;
        candidate.WarnFewFreeSlots = WarnFewFreeSlots;
        candidate.AutoBackupBeforeArchive = AutoBackupBeforeArchive;
        candidate.AutoBackupOnChange = AutoBackupOnChange;
        candidate.AutoBackupPeriodic = AutoBackupPeriodic;
        candidate.AutoBackupPeriodDays = _autoBackupPeriodDays;
        candidate.AutoBackupKeep = _autoBackupKeep;

        bool saved = await AppSettingsService.SaveImmediateAsync(candidate);
        if (!saved) return SettingsSaveResult.PersistFailed;

        bool languageChanged = Language != AppLocalisationService.AppLanguage;
        _settings.CopyFrom(candidate);
        AppThemes.Apply(ThemeMode);
        if (languageChanged) AppLocalisationService.AppLanguage = Language;

        if (SavePath != _initialSavePath) SavePathChanged = true;

        CommitSaved();
        return languageChanged ? SettingsSaveResult.SavedLanguageChanged : SettingsSaveResult.Saved;
    }

    private void CommitSaved()
    {
        _savedThemeMode = ThemeMode;
        _savedLanguage = Language;
        _savedCarTextLanguage = CarTextLanguage;
        _savedGamePath = GamePath;
        _savedSavePath = SavePath;
        _savedAutoRefreshLiveries = AutoRefreshLiveries;
        _savedRefreshOnButtonClick = RefreshOnButtonClick;
        _savedSearchByFolderName = SearchByFolderName;
        _savedPossibleDuplicateThreshold = _possibleDuplicateThreshold;
        _savedServerConnectionEnabled = ServerConnectionEnabled;
        _savedRowsMode = RowsMode;
        _savedKeyInterval = _keyInterval;
        _savedShowChangesSinceLastLaunch = ShowChangesSinceLastLaunch;
        _savedWarnFewFreeSlots = WarnFewFreeSlots;
        _savedAutoBackupBeforeArchive = AutoBackupBeforeArchive;
        _savedAutoBackupOnChange = AutoBackupOnChange;
        _savedAutoBackupPeriodic = AutoBackupPeriodic;
        _savedAutoBackupPeriodDays = _autoBackupPeriodDays;
        _savedAutoBackupKeep = _autoBackupKeep;
        OnPropertyChanged(nameof(IsDirty));
    }
}
