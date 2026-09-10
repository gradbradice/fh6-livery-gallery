namespace LiveryGallery.Models;

internal readonly record struct GalleryStatistics(
    int FavoritesShown,
    int DuplicatesShown,
    int PossibleDuplicatesShown);
