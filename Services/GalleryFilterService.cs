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
        bool onlyMine,
        DuplicatesFilterMode duplicatesFilterMode,
        GeneratedFilterMode generatedFilterMode)
    {
        IEnumerable<LiveryEntry> query = allEntries;

        string trimmedSearch = search?.Trim() ?? "";
        if (trimmedSearch.Length > 0)
        {
            var searchTokens = trimmedSearch.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(x => x.MatchesSearch(searchTokens));
        }

        if (selectedTags.Count > 0)
            query = query.Where(x => selectedTags.All(t => x.TagsSet.Contains(t)));

        if (onlyFavorites)
            query = query.Where(x => x.IsFavorite);

        if (onlyMine)
            query = query.Where(x => x.IsMine);

        query = duplicatesFilterMode switch
        {
            DuplicatesFilterMode.DuplicatesOnly => query.Where(x => x.IsDuplicate),
            DuplicatesFilterMode.DuplicatesAndPossible => query.Where(x => x.IsDuplicate || x.IsPossibleDuplicate),
            _ => query
        };

        if (generatedFilterMode == GeneratedFilterMode.GeneratedOnly)
            query = query.Where(x => x.IsPossiblyGenerated);

        return [.. query];
    }
}
