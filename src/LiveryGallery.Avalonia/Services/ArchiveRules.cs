namespace LiveryGallery.Services;

/// <summary>Why a livery may not go to the archive</summary>
internal enum ArchiveBlock
{
    /// <summary>It may go</summary>
    None,

    /// <summary>The user locked it: it stays until they unlock it</summary>
    Locked,

    /// <summary>It is applied to a car in the game: the car would lose it</summary>
    Installed,

    /// <summary>
    /// An auction livery while the list of applied liveries is unknown. It is bound to a car the player bought at the
    /// auction house, so it may go only when the livery server's list shows that no car uses it
    /// </summary>
    AuctionNeedsInstalledList,
}

/// <summary>Which liveries may go to the archive</summary>
internal static class ArchiveRules
{
    /// <param name="isLocked">The user locked it (<see cref="LockService"/>)</param>
    /// <param name="isAuction">A <c>SoulBoundLivery_</c> folder</param>
    /// <param name="isInstalled">The livery server's list says a car uses it</param>
    /// <param name="installedListKnown">The app has that list (the server connection is on and a sync finished)</param>
    public static ArchiveBlock Check(bool isLocked, bool isAuction, bool isInstalled, bool installedListKnown) =>
        isLocked ? ArchiveBlock.Locked
        : isInstalled ? ArchiveBlock.Installed
        : isAuction && !installedListKnown ? ArchiveBlock.AuctionNeedsInstalledList
        : ArchiveBlock.None;
}
