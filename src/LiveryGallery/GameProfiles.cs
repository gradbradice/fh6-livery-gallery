using ForzaToolkit.Games.FH6;

namespace LiveryGallery;

/// <summary>
/// The games this program shows, built from the open ForzaToolkit packages. The gallery (LiveryGallery.Avalonia) gets
/// a profile and knows no game itself; another program (an FH4 gallery) builds its own here.
/// </summary>
internal static class GameProfiles
{
    /// <summary>
    /// Forza Horizon 6: Xbox app saves, a folder per livery, the auction house, the livery server and the My Designs
    /// grid of ForzaToolkit.Games.FH6
    /// </summary>
    public static GameProfile Fh6() => new()
    {
        Game = ForzaGames.FH6,
        Saves = PcGameSaveStore.ForGame(ForzaGames.FH6),
        Liveries = new Fh6LiveryStore(),
        PreviewFileNames = Fh6LiveryFiles.PreviewFileNames,
        Features = new GameFeatures { Auction = true, LiveryServer = true },
        MyDesigns = new MyDesignsGrid(Fh6Rows, MyDesignsLayout.FromSaveFolder, DefaultRows: 2),
        ListingPreviews = new ListingPreviewCache(AuctionThumbnailResolver.FindCacheDirectory, AuctionThumbnailResolver.ManifestFileName),
    };

    /// <summary>FH6's My Designs rows: two in the base game; a menu mod's scene may make them three</summary>
    internal static GridRows Fh6Rows(string? gameDirectory)
    {
        var found = MyDesignsRowsDetector.Detect(gameDirectory);
        return found.Reason switch
        {
            RowsDetectionReason.VanillaScene => new GridRows(found.Rows, GridRowsSource.Game),
            RowsDetectionReason.SceneOverride => new GridRows(found.Rows, GridRowsSource.Mod, found.SceneWriteUtc),
            RowsDetectionReason.GameFolderUnknown => new GridRows(null, GridRowsSource.GameFolderUnknown),
            _ => new GridRows(null, GridRowsSource.Unknown, found.SceneWriteUtc),
        };
    }
}
