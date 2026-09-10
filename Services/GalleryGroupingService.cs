using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class GalleryGroupingService
{
    public static List<LiveryGroup> Group(
        List<LiveryEntry> filtered,
        SortMode sortMode,
        FavoriteMode favoriteMode,
        bool groupingEnabled,
        double groupWidth)
    {
        bool onlyFavorites = favoriteMode == FavoriteMode.OnlyFavorites;
        bool favoritesFirst = favoriteMode == FavoriteMode.FavoritesFirst;
        bool separateFavorites = favoriteMode == FavoriteMode.FavoritesSeparately;

        if (!groupingEnabled)
        {
            var singleGroupItems = SortForCurrentMode(filtered, sortMode);
            if (favoritesFirst)
                singleGroupItems = [.. singleGroupItems.OrderByDescending(x => x.IsFavorite)];

            return filtered.Count > 0
                ? [new LiveryGroup
                {
                    Key = Strings.AllLiveriesGroupName,
                    Items = singleGroupItems,
                    GroupWidth = groupWidth,
                    SpecialKind = LiveryGroupSpecialKind.AllLiveries
                }]
                : [];
        }

        if (separateFavorites)
        {
            var favoriteItems = filtered.Where(x => x.IsFavorite).ToList();
            var restItems = onlyFavorites ? new List<LiveryEntry>() : filtered.Where(x => !x.IsFavorite).ToList();

            var groups = new List<LiveryGroup>();
            if (favoriteItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateFavoritesGroupName,
                    Items = SortForCurrentMode(favoriteItems, sortMode),
                    GroupWidth = groupWidth,
                    IsFavoritesGroup = true
                });
            }
            groups.AddRange(BuildGroups(restItems, sortMode, favoritesFirst: false, groupWidth));
            return groups;
        }

        return BuildGroups(filtered, sortMode, favoritesFirst, groupWidth);
    }

    private static List<LiveryGroup> BuildGroups(List<LiveryEntry> items, SortMode sortMode, bool favoritesFirst, double groupWidth)
    {
        if (sortMode == SortMode.Author)
        {
            return [.. items
                .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new LiveryGroup
                {
                    Key = g.Key,
                    Items = OrderGroupItems(g, favoritesFirst,
                            x => x.CarManufacturer, x => x.CarModelName, x => x.LiveryName),
                    GroupWidth = groupWidth
                })];
        }

        if (sortMode == SortMode.DownloadTime)
        {
            return [.. items
                .GroupBy(x => x.DownloadYearMonth)
                .OrderByDescending(g => g.Key ?? DateTime.MinValue)
                .Select(g => new LiveryGroup
                {
                    Key = g.Key is { } month
                        ? month.ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture)
                        : Strings.UnknownDownloadDate,
                    SpecialKind = g.Key is not null ? LiveryGroupSpecialKind.DownloadMonth : LiveryGroupSpecialKind.UnknownDownloadDate,
                    SpecialMonth = g.Key,
                    Items = [.. (favoritesFirst
                                ? g.OrderByDescending(x => x.IsFavorite).ThenByDescending(x => x.DownloadDate ?? DateTime.MinValue)
                                : g.OrderByDescending(x => x.DownloadDate ?? DateTime.MinValue))
                            .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
                    GroupWidth = groupWidth
                })];
        }

        string unknownLabel = Strings.UnknownManufacturer;
        return [.. items
            .GroupBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Equals(unknownLabel, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LiveryGroup
            {
                Key = g.Key,
                SpecialKind = g.Key.Equals(unknownLabel, StringComparison.OrdinalIgnoreCase)
                    ? LiveryGroupSpecialKind.UnknownManufacturer
                    : LiveryGroupSpecialKind.None,
                Items = [.. (favoritesFirst
                            ? g.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                            : g.OrderBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase))
                        .ThenBy(x => x.CarYear)
                        .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
                GroupWidth = groupWidth
            })];
    }

    private static List<LiveryEntry> SortForCurrentMode(List<LiveryEntry> items, SortMode sortMode) => sortMode switch
    {
        SortMode.Author =>
            [.. items.OrderBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
        SortMode.DownloadTime =>
            [.. items.OrderByDescending(x => x.DownloadDate ?? DateTime.MinValue)
                  .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
        _ => [.. items.OrderBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(x => x.CarYear)
                  .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
    };

    private static List<LiveryEntry> OrderGroupItems(
        IEnumerable<LiveryEntry> items,
        bool favoritesFirst,
        Func<LiveryEntry, string> key1,
        Func<LiveryEntry, string> key2,
        Func<LiveryEntry, string> key3)
    {
        var ordered = favoritesFirst
            ? items.OrderByDescending(x => x.IsFavorite).ThenBy(key1, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(key1, StringComparer.OrdinalIgnoreCase);

        return [.. ordered
            .ThenBy(key2, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key3, StringComparer.OrdinalIgnoreCase)];
    }
}
