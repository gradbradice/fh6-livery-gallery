using Avalonia;
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
        // The process's cultures (ForzaToolkit.AppHost); the ForzaToolkit dialogs follow the UI culture
        var cultureInfo = AppLanguages.Apply(language);
        _culture = cultureInfo;
        Strings.Culture = cultureInfo;
        // The toolkit dialogs' buttons (Yes, No, OK): the UI culture set here does not reach code that runs later in
        // another async flow (a dialog opened after the settings were saved), so their language is set outright
        ForzaToolkit.UI.Localisation.UiStrings.Culture = cultureInfo;

        UpdateCardResourceStrings();
    }

    private static void UpdateCardResourceStrings()
    {
        var app = Application.Current;
        if (app is null) return;

        // The tooltips of a card's buttons show the key of their action
        app.Resources["Loc_FavoriteToggleTooltip"] = new ShortcutTip(Strings.FavoriteToggleTooltip, GalleryShortcut.Favorite);
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
        app.Resources["Loc_EditTagsTooltip"] = new ShortcutTip(Strings.EditTagsTooltip, GalleryShortcut.Tags);
        app.Resources["Loc_TagChipTooltip"] = Strings.TagChipTooltip;
        app.Resources["Loc_ViewPreviewTooltip"] = new ShortcutTip(Strings.ViewPreviewTooltip, GalleryShortcut.Preview);
        app.Resources["Loc_ClearSelectionLabel"] = Strings.ClearSelectionLabel;
        app.Resources["Loc_ClearSelectionTip"] = new ShortcutTip(Strings.ClearSelectionLabel, GalleryShortcut.Escape);
        app.Resources["Loc_AuctionBadgeLabel"] = Strings.AuctionBadgeLabel;
        app.Resources["Loc_InstalledBadgeLabel"] = Strings.InstalledBadgeLabel;
        app.Resources["Loc_AuctionBadgeTooltip"] = Strings.AuctionBadgeTooltip;
        app.Resources["Loc_ContextMenuMoveToArchive"] = Strings.ContextMenuMoveToArchive;
        app.Resources["Loc_ContextMenuNavigateInGame"] = Strings.ContextMenuNavigateInGame;
        app.Resources["Loc_ContextMenuOnlyThisCar"] = Strings.ContextMenuOnlyThisCar;
        app.Resources["Loc_ContextMenuOnlyThisManufacturer"] = Strings.ContextMenuOnlyThisManufacturer;
        app.Resources["Loc_ContextMenuOnlyThisAuthor"] = Strings.ContextMenuOnlyThisAuthor;
        app.Resources["Loc_ContextMenuAssignAuthor"] = Strings.ContextMenuAssignAuthor;
        app.Resources["Loc_ContextMenuUnassignAuthor"] = Strings.ContextMenuUnassignAuthor;
        app.Resources["Loc_ContextMenuOnlyThisCountry"] = Strings.ContextMenuOnlyThisCountry;
        app.Resources["Loc_ContextMenuOnlyThisCarType"] = Strings.ContextMenuOnlyThisCarType;
        app.Resources["Loc_ContextMenuClearQuickFilters"] = Strings.ContextMenuClearQuickFilters;
        app.Resources["Loc_QuickFilterRemoveTooltip"] = Strings.QuickFilterRemoveTooltip;
        app.Resources["Loc_QuickFilterAddTooltip"] = Strings.QuickFilterAddTooltip;
        app.Resources["Loc_QuickFilterKindManufacturer"] = Strings.QuickFilterKindManufacturer;
        app.Resources["Loc_QuickFilterKindCar"] = Strings.QuickFilterKindCar;
        app.Resources["Loc_QuickFilterKindAuthor"] = Strings.QuickFilterKindAuthor;
        app.Resources["Loc_QuickFilterKindCountry"] = Strings.SortOptionCountry;
        app.Resources["Loc_QuickFilterKindCarType"] = Strings.SortOptionCarType;
        app.Resources["Loc_QuickFilterSearchWatermark"] = Strings.QuickFilterSearchWatermark;
        app.Resources["Loc_QuickFilterPickerEmpty"] = Strings.QuickFilterPickerEmpty;
        app.Resources["Loc_GroupShowOnlyButton"] = Strings.GroupShowOnlyButton;
        app.Resources["Loc_GroupShowOnlyTooltip"] = Strings.GroupShowOnlyTooltip;
    }
}
