using LiveryGallery.Enums;

namespace LiveryGallery.ViewModels;

internal readonly record struct GalleryFilterState(
    string? SearchText,
    SortMode SortMode,
    FavoriteMode FavoriteMode,
    MineMode MineMode,
    DuplicatesFilterMode DuplicatesFilterMode,
    GeneratedFilterMode GeneratedFilterMode,
    PaintFilterMode PaintFilterMode,
    bool GroupingEnabled,
    bool SearchByFolderName);
