using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>
/// The gallery's groups: <see cref="GalleryGrouping"/> of ForzaToolkit.Liveries, named here in the user's language
/// </summary>
internal static class GalleryGroupingService
{
    public static List<LiveryGroup> Group(
        List<LiveryEntry> filtered,
        SortMode sortMode,
        FavoriteMode favoriteMode,
        MineMode mineMode,
        AuctionFilterMode auctionMode,
        bool groupingEnabled,
        double groupWidth,
        InstalledMode installedMode = InstalledMode.None)
    {
        var groups = GalleryGrouping.Group(filtered, new GalleryGroupingOptions
        {
            Sort = sortMode,
            Favorites = favoriteMode,
            Mine = mineMode,
            Auction = auctionMode,
            Installed = installedMode,
            GroupingEnabled = groupingEnabled,
        });
        return [.. groups.Select(g => ToViewGroup(g, groupWidth))];
    }

    private static LiveryGroup ToViewGroup(GalleryGroup<LiveryEntry> group, double groupWidth) => new()
    {
        Key = Name(group),
        Items = [.. group.Items],
        GroupWidth = groupWidth,
        SpecialKind = group.Kind switch
        {
            GalleryGroupKind.AllLiveries => LiveryGroupSpecialKind.AllLiveries,
            GalleryGroupKind.UnknownManufacturer => LiveryGroupSpecialKind.UnknownManufacturer,
            GalleryGroupKind.UnknownAuthor => LiveryGroupSpecialKind.UnknownAuthor,
            GalleryGroupKind.UnknownCountry => LiveryGroupSpecialKind.UnknownCountry,
            GalleryGroupKind.UnknownCarType => LiveryGroupSpecialKind.UnknownCarType,
            GalleryGroupKind.DownloadMonth => LiveryGroupSpecialKind.DownloadMonth,
            GalleryGroupKind.UnknownDownloadDate => LiveryGroupSpecialKind.UnknownDownloadDate,
            GalleryGroupKind.NotInMyDesigns => LiveryGroupSpecialKind.NotInMyDesigns,
            _ => LiveryGroupSpecialKind.None,
        },
        SpecialMonth = group.Month,
        GroupFilter = group.Filter,
        IsFavoritesGroup = group.Kind == GalleryGroupKind.Favorites,
        IsMineGroup = group.Kind == GalleryGroupKind.Mine || group.AllMine,
        IsAuctionGroup = group.Kind == GalleryGroupKind.Auction,
    };

    /// <summary>The group's title; MainWindow.Localization renames the special ones when the language changes</summary>
    private static string Name(GalleryGroup<LiveryEntry> group) => group.Kind switch
    {
        GalleryGroupKind.AllLiveries => Strings.AllLiveriesGroupName,
        GalleryGroupKind.Favorites => Strings.SeparateFavoritesGroupName,
        GalleryGroupKind.Mine => Strings.SeparateMineGroupName,
        GalleryGroupKind.Auction => Strings.SeparateAuctionGroupName,
        GalleryGroupKind.UnknownManufacturer => Strings.UnknownManufacturer,
        GalleryGroupKind.UnknownAuthor => Strings.UnknownAuthor,
        GalleryGroupKind.UnknownCountry => Strings.UnknownCountry,
        GalleryGroupKind.UnknownCarType => Strings.UnknownCarType,
        GalleryGroupKind.UnknownDownloadDate => Strings.UnknownDownloadDate,
        GalleryGroupKind.NotInMyDesigns => Strings.GroupNotInMyDesigns,
        GalleryGroupKind.DownloadMonth when group.Month is { } month =>
            month.ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture),
        GalleryGroupKind.Car when group.Filter is { } car => car.FullDisplayText,
        _ => group.Name ?? "",
    };
}
