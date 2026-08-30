using LiveryGallery.Enums;
using System.Globalization;

namespace LiveryGallery.Services;

internal static class AppLocalisationService
{
    private static AppLanguage _appLanguage = AppLanguage.English;

    public static AppLanguage AppLanguage
    {
        get => _appLanguage;
        set
        {
            _appLanguage = value;
            ApplyCulture(value);
        }
    }

    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string MonthYearFormat => _appLanguage switch
    {
        AppLanguage.Japanese or AppLanguage.ChineseTraditional or AppLanguage.ChineseSimplified => "yyyy年MMMM",
        AppLanguage.Korean => "yyyy년 MMMM",
        _ => "MMMM yyyy",
    };

    private static void ApplyCulture(AppLanguage language)
    {
        string culture = AppLanguageToString(language);
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    public static AppLanguage GetSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        string iso = culture.TwoLetterISOLanguageName.ToLowerInvariant();

        if (iso == "zh")
        {
            string fullName = culture.Name.ToLowerInvariant();
            bool isTraditional = fullName.Contains("hant")
                || fullName.EndsWith("-tw") || fullName.Contains("-tw-")
                || fullName.EndsWith("-hk") || fullName.Contains("-hk-")
                || fullName.EndsWith("-mo") || fullName.Contains("-mo-");
            return isTraditional ? AppLanguage.ChineseTraditional : AppLanguage.ChineseSimplified;
        }

        return StringToAppLanguage(iso);
    }

    private static AppLanguage StringToAppLanguage(string language)
    {
        return language switch
        {
            "en" => AppLanguage.English,
            "ru" => AppLanguage.Russian,
            "ja" => AppLanguage.Japanese,
            "de" => AppLanguage.German,
            "fr" => AppLanguage.French,
            "ko" => AppLanguage.Korean,
            "es" => AppLanguage.Spanish,
            "it" => AppLanguage.Italian,
            "pt" => AppLanguage.Portuguese,
            _ => AppLanguage.English,
        };
    }

    private static string AppLanguageToString(AppLanguage language)
    {
        return language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Russian => "ru",
            AppLanguage.Japanese => "ja",
            AppLanguage.German => "de",
            AppLanguage.French => "fr",
            AppLanguage.ChineseTraditional => "zh-Hant",
            AppLanguage.ChineseSimplified => "zh-Hans",
            AppLanguage.Korean => "ko",
            AppLanguage.Spanish => "es",
            AppLanguage.Italian => "it",
            AppLanguage.Portuguese => "pt",
            _ => "en",
        };
    }
}
