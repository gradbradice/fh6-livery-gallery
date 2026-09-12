using LiveryGallery.Enums;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class GalleryFilterService
{
    public static List<LiveryEntry> Apply(
        List<LiveryEntry> allEntries,
        string? search,
        IReadOnlyCollection<string> selectedTags,
        bool onlyFavorites,
        DuplicatesFilterMode duplicatesFilterMode)
    {
        IEnumerable<LiveryEntry> query = allEntries;

        string trimmedSearch = search?.Trim() ?? "";
        if (trimmedSearch.Length > 0)
        {
            var searchTokens = trimmedSearch.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(x => x.MatchesSearch(searchTokens));
        }

        if (selectedTags.Count > 0)
            query = query.Where(x => selectedTags.All(t => x.Tags.Any(xt => xt.Equals(t, StringComparison.OrdinalIgnoreCase))));

        if (onlyFavorites)
            query = query.Where(x => x.IsFavorite);

        query = duplicatesFilterMode switch
        {
            DuplicatesFilterMode.DuplicatesOnly => query.Where(x => x.IsDuplicate),
            DuplicatesFilterMode.DuplicatesAndPossible => query.Where(x => x.IsDuplicate || x.IsPossibleDuplicate),
            _ => query
        };

        return [.. query];
    }
}
