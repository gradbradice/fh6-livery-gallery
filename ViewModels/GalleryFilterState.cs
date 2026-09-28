using LiveryGallery.Enums;
using LiveryGallery.Models;

namespace LiveryGallery.ViewModels;

internal readonly record struct GalleryFilterState(
    string? SearchText,
    SortMode SortMode,
    FavoriteMode FavoriteMode,
    MineMode MineMode,
    InstalledMode InstalledMode,
    DuplicatesFilterMode DuplicatesFilterMode,
    GeneratedFilterMode GeneratedFilterMode,
    PaintFilterMode PaintFilterMode,
    AuctionFilterMode AuctionFilterMode,
    bool GroupingEnabled,
    bool SearchByFolderName,
    IReadOnlyList<QuickFilter>? QuickFilters);
