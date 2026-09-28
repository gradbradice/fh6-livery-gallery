using Avalonia;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using System.Globalization;

namespace LiveryGallery.Services;

internal static class AppLocalisationService
{
    private static AppLanguage _appLanguage = AppLanguage.English;
    private static CultureInfo _culture = CultureInfo.CurrentCulture;
    public static event Action? LanguageChanged;

    public static AppLanguage AppLanguage
    {
        get => _appLanguage;
        set
        {
            bool changed = _appLanguage != value;
            _appLanguage = value;
            ApplyCulture(value);
            if (changed) LanguageChanged?.Invoke();
        }
    }

    public static CultureInfo Culture => _culture;

    public static string MonthYearFormat => _appLanguage switch
    {
        AppLanguage.Japanese or AppLanguage.ChineseTraditional or AppLanguage.ChineseSimplified => "yyyy年MMMM",
        AppLanguage.Korean => "yyyy년 MMMM",
        _ => "MMMM yyyy",
    };

    private static void ApplyCulture(AppLanguage language)
    {
        string culture = AppLanguageToString(language);
        var cultureInfo = new CultureInfo(culture);
        _culture = cultureInfo;

        // try to fix language change
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        Strings.Culture = cultureInfo;

        UpdateCardResourceStrings();
    }

    private static void UpdateCardResourceStrings()
    {
        var app = Application.Current;
        if (app is null) return;

        app.Resources["Loc_FavoriteToggleTooltip"] = Strings.FavoriteToggleTooltip;
        app.Resources["Loc_DuplicateBadgeTooltip"] = Strings.DuplicateBadgeTooltip;
        app.Resources["Loc_DuplicateBadgeLabel"] = Strings.DuplicateBadgeLabel;
        app.Resources["Loc_PossibleDuplicateBadgeTooltip"] = Strings.PossibleDuplicateBadgeTooltip;
        app.Resources["Loc_PossibleDuplicateBadgeLabel"] = Strings.PossibleDuplicateBadgeLabel;
        app.Resources["Loc_MyLiveryBadgeTooltip"] = Strings.MyLiveryBadgeTooltip;
        app.Resources["Loc_MyLiveryBadgeLabel"] = Strings.MyLiveryBadgeLabel;
        app.Resources["Loc_PossiblyGeneratedBadgeLabel"] = Strings.PossiblyGeneratedBadgeLabel;
        app.Resources["Loc_PossiblyGeneratedBadgeTooltip"] = Strings.PossiblyGeneratedBadgeTooltip;
        app.Resources["Loc_PaintBadgeLabel"] = Strings.PaintBadgeLabel;
        app.Resources["Loc_PaintBadgeTooltip"] = Strings.PaintBadgeTooltip;
        app.Resources["Loc_ParseErrorBadgeLabel"] = Strings.ParseErrorBadgeLabel;
        app.Resources["Loc_ParseErrorBadgeTooltip"] = Strings.ParseErrorBadgeTooltip;
        app.Resources["Loc_ParsePartialBadgeLabel"] = Strings.ParsePartialBadgeLabel;
        app.Resources["Loc_AuthorLabel"] = Strings.AuthorLabel;
        app.Resources["Loc_DateLabel"] = Strings.DateLabel;
        app.Resources["Loc_EditTagsTooltip"] = Strings.EditTagsTooltip;
        app.Resources["Loc_ViewPreviewTooltip"] = Strings.ViewPreviewTooltip;
        app.Resources["Loc_ClearSelectionLabel"] = Strings.ClearSelectionLabel;
        app.Resources["Loc_AuctionBadgeLabel"] = Strings.AuctionBadgeLabel;
        app.Resources["Loc_InstalledBadgeLabel"] = Strings.InstalledBadgeLabel;
        app.Resources["Loc_AuctionBadgeTooltip"] = Strings.AuctionBadgeTooltip;
        app.Resources["Loc_ContextMenuMoveToArchive"] = Strings.ContextMenuMoveToArchive;
        app.Resources["Loc_ContextMenuOnlyThisCar"] = Strings.ContextMenuOnlyThisCar;
        app.Resources["Loc_ContextMenuOnlyThisManufacturer"] = Strings.ContextMenuOnlyThisManufacturer;
        app.Resources["Loc_ContextMenuOnlyThisAuthor"] = Strings.ContextMenuOnlyThisAuthor;
        app.Resources["Loc_ContextMenuClearQuickFilters"] = Strings.ContextMenuClearQuickFilters;
        app.Resources["Loc_QuickFilterRemoveTooltip"] = Strings.QuickFilterRemoveTooltip;
        app.Resources["Loc_QuickFilterAddTooltip"] = Strings.QuickFilterAddTooltip;
        app.Resources["Loc_QuickFilterKindManufacturer"] = Strings.QuickFilterKindManufacturer;
        app.Resources["Loc_QuickFilterKindCar"] = Strings.QuickFilterKindCar;
        app.Resources["Loc_QuickFilterKindAuthor"] = Strings.QuickFilterKindAuthor;
        app.Resources["Loc_QuickFilterSearchWatermark"] = Strings.QuickFilterSearchWatermark;
        app.Resources["Loc_QuickFilterPickerEmpty"] = Strings.QuickFilterPickerEmpty;
        app.Resources["Loc_GroupShowOnlyButton"] = Strings.GroupShowOnlyButton;
        app.Resources["Loc_GroupShowOnlyTooltip"] = Strings.GroupShowOnlyTooltip;
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

    public static AppLanguage StringToAppLanguage(string language)
    {
        return language switch
        {
            "en" => AppLanguage.English,
            "ru" => AppLanguage.Russian,
            "ja" => AppLanguage.Japanese,
            "de" => AppLanguage.German,
            "fr" => AppLanguage.French,
            "zh-Hant" => AppLanguage.ChineseTraditional,
            "zh-Hans" => AppLanguage.ChineseSimplified,
            "ko" => AppLanguage.Korean,
            "es" => AppLanguage.Spanish,
            "it" => AppLanguage.Italian,
            "pt" => AppLanguage.Portuguese,
            _ => AppLanguage.English,
        };
    }

    public static string AppLanguageToString(AppLanguage language)
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
