using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>The gallery's filters: <see cref="GalleryFiltering"/> of ForzaToolkit.Liveries over the view models</summary>
internal static class GalleryFilterService
{
    public static List<LiveryEntry> Apply(
        List<LiveryEntry> allEntries,
        string? search,
        IReadOnlyCollection<string> selectedTags,
        IReadOnlyCollection<string> excludedTags,
        bool onlyFavorites,
        bool onlyMine,
        DuplicatesFilterMode duplicatesFilterMode,
        GeneratedFilterMode generatedFilterMode,
        PaintFilterMode paintFilterMode,
        bool searchByFolderName,
        IReadOnlyList<QuickFilter>? quickFilters = null,
        AuctionFilterMode auctionFilterMode = AuctionFilterMode.All,
        bool onlyInstalled = false) =>
        GalleryFiltering.Apply(allEntries, new GalleryFilter
        {
            Search = search,
            SearchByFolderName = searchByFolderName,
            Tags = selectedTags,
            ExcludedTags = excludedTags,
            QuickFilters = quickFilters,
            OnlyFavorites = onlyFavorites,
            OnlyMine = onlyMine,
            OnlyInstalled = onlyInstalled,
            Duplicates = duplicatesFilterMode,
            Generated = generatedFilterMode,
            Paint = paintFilterMode,
            Auction = auctionFilterMode,
        });
}
