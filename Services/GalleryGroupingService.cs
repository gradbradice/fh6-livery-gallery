using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

internal static class GalleryGroupingService
{
    private readonly record struct PinFirst(bool Favorites, bool Installed, bool Mine);

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
        var first = new PinFirst(
            Favorites: favoriteMode == FavoriteMode.FavoritesFirst,
            Installed: installedMode == InstalledMode.InstalledFirst,
            Mine: mineMode == MineMode.MineFirst);
        bool separateFavorites = favoriteMode == FavoriteMode.FavoritesSeparately;
        bool separateMine = mineMode == MineMode.MineSeparately;
        bool separateAuction = auctionMode == AuctionFilterMode.AuctionSeparately;

        if (!groupingEnabled)
        {
            var singleGroupItems = SortForCurrentMode(filtered, sortMode, first);

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

        if (separateFavorites || separateMine || separateAuction)
        {
            var favoriteItems = new List<LiveryEntry>();
            var mineItems = new List<LiveryEntry>();
            var auctionItems = new List<LiveryEntry>();
            var restItems = new List<LiveryEntry>();
            foreach (var item in filtered)
            {
                if (separateFavorites && item.IsFavorite) favoriteItems.Add(item);
                else if (separateMine && item.IsMine) mineItems.Add(item);
                else if (separateAuction && item.IsAuction) auctionItems.Add(item);
                else restItems.Add(item);
            }

            var groups = new List<LiveryGroup>();
            if (favoriteItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateFavoritesGroupName,
                    Items = SortForCurrentMode(favoriteItems, sortMode, first with { Favorites = false }),
                    GroupWidth = groupWidth,
                    IsFavoritesGroup = true
                });
            }
            if (mineItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateMineGroupName,
                    Items = SortForCurrentMode(mineItems, sortMode, first with { Favorites = false, Mine = false }),
                    GroupWidth = groupWidth,
                    IsMineGroup = true
                });
            }
            if (auctionItems.Count > 0)
            {
                groups.Add(new LiveryGroup
                {
                    Key = Strings.SeparateAuctionGroupName,
                    Items = SortForCurrentMode(auctionItems, sortMode, first with { Mine = false }),
                    GroupWidth = groupWidth,
                    IsAuctionGroup = true
                });
            }
            groups.AddRange(BuildGroups(restItems, sortMode, first with { Favorites = false, Mine = false }, groupWidth));
            return groups;
        }

        return BuildGroups(filtered, sortMode, first, groupWidth);
    }

    private static List<LiveryGroup> BuildGroups(
        List<LiveryEntry> items, SortMode sortMode, PinFirst first, double groupWidth)
    {
        if (sortMode == SortMode.Author)
        {
            return [.. items
                .GroupBy(x => (IsUnknown: x.Data.IsAuthorUnknown, Name: x.Data.IsAuthorUnknown ? "" : x.Author),
                    UnknownFlagAndNameComparer.Instance)
                .OrderBy(g => g.Key.IsUnknown)
                .ThenBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new LiveryGroup
                {
                    Key = g.Key.IsUnknown ? Strings.UnknownAuthor : g.First().Author,
                    SpecialKind = g.Key.IsUnknown ? LiveryGroupSpecialKind.UnknownAuthor : LiveryGroupSpecialKind.None,
                    GroupFilter = QuickFilter.ForAuthor(g.First()),
                    Items = OrderGroupItems(g, first,
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
                    Items = [.. OrderPinnedFirst(g, first, x => x.DownloadDate ?? DateTime.MinValue, descending: true)
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
                GroupFilter = QuickFilter.ForManufacturer(g.First()),
                Items = [.. OrderPinnedFirst(g, first, x => x.CarModelName, descending: false, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.CarYear)
                        .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
                GroupWidth = groupWidth
            })];
    }

    private static List<LiveryEntry> SortForCurrentMode(List<LiveryEntry> items, SortMode sortMode, PinFirst first)
    {
        return sortMode switch
        {
            SortMode.Author => [.. OrderPinnedFirst(items, first, x => x.Data.IsAuthorUnknown, descending: false)
                    .ThenBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
            SortMode.DownloadTime => [.. OrderPinnedFirst(items, first, x => x.DownloadDate ?? DateTime.MinValue, descending: true)
                    .ThenBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
            _ => [.. OrderPinnedFirst(items, first, x => x.CarManufacturer, descending: false, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarModelName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.CarYear)
                    .ThenBy(x => x.LiveryName, StringComparer.OrdinalIgnoreCase)],
        };
    }

    private static List<LiveryEntry> OrderGroupItems(
        IEnumerable<LiveryEntry> items,
        PinFirst first,
        Func<LiveryEntry, string> key1,
        Func<LiveryEntry, string> key2,
        Func<LiveryEntry, string> key3)
    {
        return [.. OrderPinnedFirst(items, first, key1, descending: false, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key2, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key3, StringComparer.OrdinalIgnoreCase)];
    }

    private sealed class UnknownFlagAndNameComparer : IEqualityComparer<(bool IsUnknown, string Name)>
    {
        public static readonly UnknownFlagAndNameComparer Instance = new();

        public bool Equals((bool IsUnknown, string Name) x, (bool IsUnknown, string Name) y) =>
            x.IsUnknown == y.IsUnknown && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode((bool IsUnknown, string Name) obj) =>
            HashCode.Combine(obj.IsUnknown, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }

    private static IOrderedEnumerable<LiveryEntry> OrderPinnedFirst<TKey>(
        IEnumerable<LiveryEntry> items, PinFirst first,
        Func<LiveryEntry, TKey> key, bool descending, IComparer<TKey>? comparer = null)
    {
        IOrderedEnumerable<LiveryEntry> ordered = first.Favorites
            ? items.OrderByDescending(x => x.IsFavorite)
            : items.OrderBy(_ => 0);

        if (first.Installed) ordered = ordered.ThenByDescending(x => x.IsInstalled);
        if (first.Mine) ordered = ordered.ThenByDescending(x => x.IsMine);

        return descending ? ordered.ThenByDescending(key, comparer) : ordered.ThenBy(key, comparer);
    }
}
