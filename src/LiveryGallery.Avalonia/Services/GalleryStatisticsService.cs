using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>The gallery's counts (<see cref="GalleryStatistics"/> of ForzaToolkit.Liveries) and their wording</summary>
internal static class GalleryStatisticsService
{
    public static GalleryCounts Calculate(List<LiveryEntry> filtered) => GalleryStatistics.Count(filtered);

    public static GalleryOverview CalculateOverall(List<LiveryEntry> allEntries) => GalleryStatistics.Overview(allEntries);

    public static string FormatOverallMessage(GalleryOverview stats)
    {
        static string Format(GalleryTop top) => top.Label is not null ? $"{top.Label} ({top.Count})" : "-";

        return string.Join("\n", new[]
        {
            $"{Strings.StatsTotalLiveries}: {stats.Total}",
            $"{Strings.StatsFavoritesCount}: {stats.FavoritesCount}",
            "",
            $"{Strings.StatsPopularManufacturer}: {Format(stats.PopularManufacturer)}",
            $"{Strings.StatsPopularModel}: {Format(stats.PopularModel)}",
            $"{Strings.StatsPopularCar}: {Format(stats.PopularCar)}",
            $"{Strings.StatsPopularAuthor}: {Format(stats.PopularAuthor)}",
            "",
            $"{Strings.StatsFavoriteManufacturer}: {Format(stats.FavoriteManufacturer)}",
            $"{Strings.StatsFavoriteModel}: {Format(stats.FavoriteModel)}",
            $"{Strings.StatsFavoriteCar}: {Format(stats.FavoriteCar)}",
            $"{Strings.StatsFavoriteAuthor}: {Format(stats.FavoriteAuthor)}",
            "",
            $"{Strings.StatsTotalDuplicates}: {stats.DuplicatesCount}",
            $"{Strings.StatsPossibleDuplicates}: {stats.PossibleDuplicatesCount}",
        });
    }
}
