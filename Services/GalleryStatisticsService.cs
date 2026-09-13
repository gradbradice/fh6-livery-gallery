using LiveryGallery.Localisation;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class GalleryStatisticsService
{
    public static GalleryStatistics Calculate(List<LiveryEntry> filtered) => new(
        filtered.Count(x => x.IsFavorite),
        filtered.Count(x => x.IsDuplicate),
        filtered.Count(x => x.IsPossibleDuplicate));

    public static GalleryOverallStatistics CalculateOverall(List<LiveryEntry> allEntries)
    {
        var favorites = allEntries.Where(x => x.IsFavorite).ToList();

        var (popManufacturer, popManufacturerCount) = TopString(allEntries, x => x.CarManufacturer);
        var (popModel, popModelCount) = TopModel(allEntries, includeYear: false);
        var (popCar, popCarCount) = TopModel(allEntries, includeYear: true);
        var (popAuthor, popAuthorCount) = TopString(allEntries, x => x.Author);

        var (favManufacturer, favManufacturerCount) = TopString(favorites, x => x.CarManufacturer);
        var (favModel, favModelCount) = TopModel(favorites, includeYear: false);
        var (favCar, favCarCount) = TopModel(favorites, includeYear: true);
        var (favAuthor, favAuthorCount) = TopString(favorites, x => x.Author);

        return new GalleryOverallStatistics(
            allEntries.Count,
            favorites.Count,
            allEntries.Count(x => x.IsDuplicate),
            allEntries.Count(x => x.IsPossibleDuplicate),
            popManufacturer, popManufacturerCount,
            popModel, popModelCount,
            popCar, popCarCount,
            popAuthor, popAuthorCount,
            favManufacturer, favManufacturerCount,
            favModel, favModelCount,
            favCar, favCarCount,
            favAuthor, favAuthorCount);
    }

    public static string FormatOverallMessage(GalleryOverallStatistics stats)
    {
        static string Format(string? label, int count) => label is not null ? $"{label} ({count})" : "-";

        return string.Join("\n", new[]
        {
            $"{Strings.StatsTotalLiveries}: {stats.Total}",
            $"{Strings.StatsFavoritesCount}: {stats.FavoritesCount}",
            "",
            $"{Strings.StatsPopularManufacturer}: {Format(stats.PopularManufacturer, stats.PopularManufacturerCount)}",
            $"{Strings.StatsPopularModel}: {Format(stats.PopularModel, stats.PopularModelCount)}",
            $"{Strings.StatsPopularCar}: {Format(stats.PopularCar, stats.PopularCarCount)}",
            $"{Strings.StatsPopularAuthor}: {Format(stats.PopularAuthor, stats.PopularAuthorCount)}",
            "",
            $"{Strings.StatsFavoriteManufacturer}: {Format(stats.FavoriteManufacturer, stats.FavoriteManufacturerCount)}",
            $"{Strings.StatsFavoriteModel}: {Format(stats.FavoriteModel, stats.FavoriteModelCount)}",
            $"{Strings.StatsFavoriteCar}: {Format(stats.FavoriteCar, stats.FavoriteCarCount)}",
            $"{Strings.StatsFavoriteAuthor}: {Format(stats.FavoriteAuthor, stats.FavoriteAuthorCount)}",
            "",
            $"{Strings.StatsTotalDuplicates}: {stats.DuplicatesCount}",
            $"{Strings.StatsPossibleDuplicates}: {stats.PossibleDuplicatesCount}",
        });
    }

    private static (string? Label, int Count) TopString(List<LiveryEntry> entries, Func<LiveryEntry, string> selector)
    {
        if (entries.Count == 0) return (null, 0);
        var group = entries
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .First();
        return (group.Key, group.Count());
    }

    private static (string? Label, int Count) TopModel(List<LiveryEntry> entries, bool includeYear)
    {
        if (entries.Count == 0) return (null, 0);

        var group = entries
            .GroupBy(
                x => (x.CarManufacturer, x.CarModelName, Year: includeYear ? x.CarYear : null),
                ModelKeyComparer.Instance)
            .OrderByDescending(g => g.Count())
            .First();

        string label = includeYear && group.Key.Year is { } year
            ? $"{group.Key.CarManufacturer} {group.Key.CarModelName} ({year})"
            : $"{group.Key.CarManufacturer} {group.Key.CarModelName}";

        return (label, group.Count());
    }

    private sealed class ModelKeyComparer : IEqualityComparer<(string CarManufacturer, string CarModelName, int? Year)>
    {
        public static readonly ModelKeyComparer Instance = new();

        public bool Equals(
            (string CarManufacturer, string CarModelName, int? Year) x,
            (string CarManufacturer, string CarModelName, int? Year) y) =>
            string.Equals(x.CarManufacturer, y.CarManufacturer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.CarModelName, y.CarModelName, StringComparison.OrdinalIgnoreCase)
            && x.Year == y.Year;

        public int GetHashCode((string CarManufacturer, string CarModelName, int? Year) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CarManufacturer),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CarModelName),
                obj.Year);
    }
}
