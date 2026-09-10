namespace LiveryGallery.Models;

internal readonly record struct GalleryOverallStatistics(
    int Total,
    int FavoritesCount,
    int DuplicatesCount,
    int PossibleDuplicatesCount,
    string? TopManufacturer,
    int TopManufacturerCount,
    string? TopAuthor,
    int TopAuthorCount);
