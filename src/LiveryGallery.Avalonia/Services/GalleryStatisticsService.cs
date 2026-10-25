using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>
/// The status line's counts (<see cref="GalleryStatistics"/> of ForzaToolkit.Liveries); the statistics window uses
/// ForzaToolkit.Liveries <c>Statistics</c> (<see cref="StatisticsViewModel"/>)
/// </summary>
internal static class GalleryStatisticsService
{
    public static GalleryCounts Calculate(List<LiveryEntry> filtered) => GalleryStatistics.Count(filtered);
}
