using ForzaToolkit.Games.Installation;
using ForzaToolkit.StringTables;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

/// <summary>Where the car texts from the game stand</summary>
internal enum GameTextsState
{
    /// <summary>The game folder has not been looked at yet in this run: the texts kept from the last run are used</summary>
    NotChecked,

    /// <summary>The game folder and its language files are there</summary>
    Available,

    /// <summary>No game folder: neither set in the settings nor found on this PC (or it is not the game folder)</summary>
    NoGameFolder,

    /// <summary>The game folder has no language files (StringTables)</summary>
    NoStringTables,
}

/// <summary>A car's texts as a livery card shows them</summary>
/// <param name="Texts">Make, model, country and car type in the language in use</param>
/// <param name="CountryRaw">The country from the car database (English): quick filters match it</param>
/// <param name="CarTypeRaw">The car type from the car database (English): quick filters match it</param>
/// <param name="UnavailableNote">Why the texts are not in the chosen language (no game folder…); null when they are</param>
internal sealed record CarTextResult(CarTexts Texts, string CountryRaw, string CarTypeRaw, string? UnavailableNote);

/// <summary>
/// The texts of a car as the gallery shows them: make, model, country and car type in the chosen game language
/// (the setting, or the game language of the interface language). The texts come from the game's own string tables,
/// kept by <see cref="CarLocalizationService"/> next to the car list in the ForzaToolkit cache; without the game folder
/// (or its language files), from the texts published with the car list (<see cref="CarDatabaseService.Texts"/>:
/// <c>strings.json</c>, <c>car-names.json</c>). Only when neither has the language are the car list's own (English)
/// texts shown, with a note why. Thread-safe.
/// </summary>
internal sealed class CarTextProvider(CarDatabaseService cars, CarLocalizationService localization, AppSettingsData settings)
{
    private volatile GameTextsState _state = GameTextsState.NotChecked;

    public CarLocalizationService Localization => localization;

    public GameTextsState State => _state;

    /// <summary>The game language the user wants: the setting, or the one of the interface</summary>
    public string WantedLanguage =>
        settings.CarTextLanguage is { Length: > 0 } chosen
            ? chosen.ToUpperInvariant()
            : GameLanguages.CodeForCulture(AppLocalisationService.Culture);

    /// <summary>The game folder gives the texts (or has not been looked at yet: the texts kept from the last run)</summary>
    private bool GameTexts => _state is not (GameTextsState.NoGameFolder or GameTextsState.NoStringTables);

    /// <summary>
    /// The language the texts are shown in: the wanted one when the game's tables or the published texts have it;
    /// otherwise, with the game folder, the one of the interface, and without it English (the car list's own texts)
    /// </summary>
    public string LanguageCode
    {
        get
        {
            string wanted = WantedLanguage;
            if ((GameTexts && localization.HasLanguage(wanted)) || cars.Texts.HasLanguage(wanted)) return wanted;
            return GameTexts ? GameLanguages.CodeForCulture(AppLocalisationService.Culture) : GameLanguages.EnglishCode;
        }
    }

    /// <summary>
    /// The game languages there are car texts in: those read from the game, else (no game folder) those published with
    /// the car list
    /// </summary>
    public IReadOnlyList<GameLanguage> Languages => GameTexts && localization.Languages.Count > 0
        ? localization.Languages
        : [.. cars.Texts.Languages.Select(GameLanguages.Get)];

    /// <summary>Why the texts are not in the wanted language; null when they are (or English is wanted)</summary>
    public string? UnavailableNote
    {
        get
        {
            if (string.Equals(WantedLanguage, GameLanguages.EnglishCode, StringComparison.OrdinalIgnoreCase)) return null;
            // The texts published with the car list have it: nothing is missing
            if (cars.Texts.HasLanguage(WantedLanguage)) return null;
            return _state switch
            {
                GameTextsState.NoGameFolder => Strings.CarTextsNoGameFolder,
                GameTextsState.NoStringTables => Strings.CarTextsNoStringTables,
                _ => null,
            };
        }
    }

    /// <summary>The texts of the car with <paramref name="carId"/>; null when the car list does not know it</summary>
    public CarTextResult? For(int carId)
    {
        if (cars.Get(carId) is not { } car) return null;
        // With the game folder its own tables first, the published texts for what they lack; without it only the published ones
        var texts = GameTexts ? localization.Localize(car, LanguageCode, cars.Texts) : cars.Texts.Localize(car, LanguageCode);
        return new CarTextResult(texts, car.Country ?? "", car.CarType ?? "", UnavailableNote);
    }

    /// <summary>
    /// Looks at the game folder (the settings', or the one found on this PC; <see cref="GameFolder.Check"/>) and reads
    /// its language archives that are new or changed. True when what is shown changes: new texts, or the folder
    /// appeared or went away. Never throws except for cancellation.
    /// </summary>
    public async Task<bool> UpdateFromGameAsync(GameProfile profile, CancellationToken ct)
    {
        string? path = string.IsNullOrWhiteSpace(settings.GameInstallPath)
            ? await profile.FindInstallFolderAsync()
            : settings.GameInstallPath;
        var report = path is null ? null : await Task.Run(() => GameFolder.Check(path, countVinyls: false), ct);

        var before = _state;
        bool textsChanged = false;
        if (report?.GameDirectory is null) _state = GameTextsState.NoGameFolder;
        else if (report.StringTablesDirectory is not { } folder)
        {
            _state = GameTextsState.NoStringTables;
            AppLog.LogWarning($"No {GameFolder.StringTablesFolderName} folder in '{report.GameDirectory}': car names come from the car list");
        }
        else
        {
            // Archives that could not be read are logged by the service; their old texts stay
            textsChanged = (await localization.UpdateAsync(folder, ct)).Changed;
            _state = GameTextsState.Available;
        }
        return textsChanged || before != _state;
    }
}
