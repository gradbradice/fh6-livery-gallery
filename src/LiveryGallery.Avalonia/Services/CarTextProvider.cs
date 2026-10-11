using ForzaToolkit.Games.Installation;
using ForzaToolkit.StringTables;
using LiveryGallery.Localisation;
using LiveryGallery.Models;

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
/// kept by <see cref="CarLocalizationService"/> next to the car list in the ForzaToolkit cache. Without the game folder
/// (or its language files) the car list's own texts are shown, whatever the language, with a note why. Thread-safe.
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

    /// <summary>
    /// The language the texts are shown in: English (the car list's own texts) without the game folder; otherwise the
    /// wanted one when the game has it, else the one of the interface
    /// </summary>
    public string LanguageCode
    {
        get
        {
            if (_state is GameTextsState.NoGameFolder or GameTextsState.NoStringTables) return GameLanguages.EnglishCode;
            string wanted = WantedLanguage;
            return localization.HasLanguage(wanted) ? wanted : GameLanguages.CodeForCulture(AppLocalisationService.Culture);
        }
    }

    /// <summary>Why the texts are not in the wanted language; null when they are (or English is wanted)</summary>
    public string? UnavailableNote
    {
        get
        {
            if (string.Equals(WantedLanguage, GameLanguages.EnglishCode, StringComparison.OrdinalIgnoreCase)) return null;
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
        return new CarTextResult(localization.Localize(car, LanguageCode), car.Country ?? "", car.CarType ?? "", UnavailableNote);
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
            AppLogger.LogWarning($"No {GameFolder.StringTablesFolderName} folder in '{report.GameDirectory}': car names come from the car list");
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
