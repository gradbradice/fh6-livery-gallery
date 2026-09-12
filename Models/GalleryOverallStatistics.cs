namespace LiveryGallery.Models;

internal readonly record struct GalleryOverallStatistics(
    int Total,
    int FavoritesCount,
    int DuplicatesCount,
    int PossibleDuplicatesCount,

    string? PopularManufacturer,
    int PopularManufacturerCount,
    string? PopularModel,
    int PopularModelCount,
    string? PopularCar,
    int PopularCarCount,
    string? PopularAuthor,
    int PopularAuthorCount,

    string? FavoriteManufacturer,
    int FavoriteManufacturerCount,
    string? FavoriteModel,
    int FavoriteModelCount,
    string? FavoriteCar,
    int FavoriteCarCount,
    string? FavoriteAuthor,
    int FavoriteAuthorCount);
