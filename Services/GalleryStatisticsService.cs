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
        int total = allEntries.Count;
        string? topManufacturer = null;
        int topManufacturerCount = 0;
        string? topAuthor = null;
        int topAuthorCount = 0;

        if (total > 0)
        {
            var manufacturerGroup = allEntries
                .GroupBy(x => x.CarManufacturer, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First();
            topManufacturer = manufacturerGroup.Key;
            topManufacturerCount = manufacturerGroup.Count();

            var authorGroup = allEntries
                .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First();
            topAuthor = authorGroup.Key;
            topAuthorCount = authorGroup.Count();
        }

        return new GalleryOverallStatistics(
            total,
            allEntries.Count(x => x.IsFavorite),
            allEntries.Count(x => x.IsDuplicate),
            allEntries.Count(x => x.IsPossibleDuplicate),
            topManufacturer, topManufacturerCount,
            topAuthor, topAuthorCount);
    }
}
