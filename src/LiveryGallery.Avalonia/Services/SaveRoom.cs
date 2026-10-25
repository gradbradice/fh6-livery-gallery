using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>How full the save is: the colour of the main window's counter and of the statistics' bar</summary>
internal enum SlotsLevel
{
    Ok,
    Warning,
    Full,
}

/// <summary>
/// The room in the save against the game's limit, one rule for the main window and the statistics. Auction liveries
/// belong to a car and are not counted
/// </summary>
internal static class SaveRoom
{
    /// <summary>From this share of the limit on, the save is almost full</summary>
    public const double WarningShare = 0.9;

    /// <summary>Fewer free slots than this at the first scan of a launch: the gallery says so once</summary>
    public const int FewFreeSlots = 20;

    public static int Counted(IEnumerable<LiveryEntry> entries) => entries.Count(e => !e.IsAuction);

    public static SlotsLevel Level(int counted, int limit) =>
        counted >= limit ? SlotsLevel.Full
        : limit > 0 && (double)counted / limit >= WarningShare ? SlotsLevel.Warning
        : SlotsLevel.Ok;

    /// <summary>Whether the first scan of a launch should tell the user how little room is left</summary>
    public static bool IsFew(int counted, int limit) => limit - counted < FewFreeSlots;
}
