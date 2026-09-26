using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

internal static class GalleryGroupingService
{
    public static List<LiveryGroup> Group(
        List<LiveryEntry> filtered,
        SortMode sortMode,
        FavoriteMode favoriteMode,
        MineMode mineMode,
        bool groupingEnabled,
        double groupWidth)
    {
        bool favoritesFirst = favoriteMode == FavoriteMode.FavoritesFirst;
        bool separateFavorites = favoriteMode == FavoriteMode.FavoritesSeparately;

        bool mineFirst = mineMode == MineMode.MineFirst;
        bool separateMine = mineMode == MineMode.MineSeparately;
        
        if (!groupingEnabled)
        {
            var singleGroupItems = SortForCurrentMode(filtered, sortMode, favoritesFirst, mineFirst);

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

        if (separateFavorites || separateMine)
        {
            var favoriteItems = new List<LiveryEntry>();
            var mineItems = new List<LiveryEntry>();
            var restItems = new List<LiveryEntry>();
            foreach (var item in filtered)
            {
                if (separateFavorites && item.IsFavorite) favoriteItems.Add(item);
                else if (separateMine && item.IsMine) mineItems.Add(item);
                else restItems.Add(item);
            }

            var groups = new List<LiveryGroup>();
            if (favoriteItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateFavoritesGroupName,
                    Items = SortForCurrentMode(favoriteItems, sortMode, favoritesFirst: false, mineFirst),
                    GroupWidth = groupWidth,
                    IsFavoritesGroup = true
                });
            }
            if (mineItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateMineGroupName,
                    Items = SortForCurrentMode(mineItems, sortMode, favoritesFirst: false, mineFirst: false),
                    GroupWidth = groupWidth,
                    IsMineGroup = true
                });
            }
            groups.AddRange(BuildGroups(restItems, sortMode, favoritesFirst: false, mineFirst: false, groupWidth));
            return groups;
        }

        return BuildGroups(filtered, sortMode, favoritesFirst, mineFirst, groupWidth);
    }

    private static List<LiveryGroup> BuildGroups(
        List<LiveryEntry> items, SortMode sortMode, bool favoritesFirst, bool mineFirst, double groupWidth)
    {
        if (sortMode == SortMode.Author)
        {
            return [.. items
                .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new LiveryGroup
                {
                    Key = g.Key,
                    Items = OrderGroupItems(g, favoritesFirst, mineFirst,
                            x => x.CarManufacturer, x => x.CarModelName, x => x.LiveryName),
                    GroupWidth = groupWidth,
                    IsMineGroup = g.All(x => x.IsMine)
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
                    Items = [.. OrderByFavoriteThenMine(g, favoritesFirst, mineFirst, x => x.DownloadDate ?? DateTime.MinValue, descending: true)
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
                Items = [.. OrderByFavoriteThenMine(g, favoritesFirst, mineFirst, x => x.CarModelName, descending: false, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.CarYear)
                        .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
                GroupWidth = groupWidth
            })];
    }

    private static List<LiveryEntry> SortForCurrentMode(List<LiveryEntry> items, SortMode sortMode, bool favoritesFirst = false, bool mineFirst = false)
    {
        return sortMode switch
        {
            SortMode.Author => [.. OrderByFavoriteThenMine(items, favoritesFirst, mineFirst, x => x.Author, descending: false, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
            SortMode.DownloadTime => [.. OrderByFavoriteThenMine(items, favoritesFirst, mineFirst, x => x.DownloadDate ?? DateTime.MinValue, descending: true)
                    .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
            _ => [.. OrderByFavoriteThenMine(items, favoritesFirst, mineFirst, x => x.CarManufacturer, descending: false, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarYear)
                    .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
        };
    }

    private static List<LiveryEntry> OrderGroupItems(
        IEnumerable<LiveryEntry> items,
        bool favoritesFirst,
        bool mineFirst,
        Func<LiveryEntry, string> key1,
        Func<LiveryEntry, string> key2,
        Func<LiveryEntry, string> key3)
    {
        return [.. OrderByFavoriteThenMine(items, favoritesFirst, mineFirst, key1, descending: false, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key2, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key3, StringComparer.OrdinalIgnoreCase)];
    }

    private static IOrderedEnumerable<LiveryEntry> OrderByFavoriteThenMine<TKey>(
        IEnumerable<LiveryEntry> items, bool favoritesFirst, bool mineFirst,
        Func<LiveryEntry, TKey> key, bool descending, IComparer<TKey>? comparer = null)
    {
        IOrderedEnumerable<LiveryEntry> ordered = favoritesFirst
            ? items.OrderByDescending(x => x.IsFavorite)
            : items.OrderBy(_ => 0);

        if (mineFirst) ordered = ordered.ThenByDescending(x => x.IsMine);

        return descending ? ordered.ThenByDescending(key, comparer) : ordered.ThenBy(key, comparer);
    }
}
