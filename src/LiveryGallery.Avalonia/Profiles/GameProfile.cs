using ForzaToolkit.Games.Installation;
using ForzaToolkit.Liveries.Scanning;

namespace LiveryGallery.Profiles;

/// <summary>What the gallery can do in a game; a feature the game does not have is not shown</summary>
internal sealed record GameFeatures
{
    /// <summary>The auction house: liveries the game only lists, their filter and group, and the archive rule for them</summary>
    public bool Auction { get; init; }

    /// <summary>The livery server that tells which liveries are installed on cars</summary>
    public bool LiveryServer { get; init; }
}

/// <summary>Where the number of rows of a game's livery menu comes from</summary>
internal enum GridRowsSource
{
    /// <summary>The game's own menu: no mod changes it</summary>
    Game,

    /// <summary>A menu mod in the game folder sets the rows</summary>
    Mod,

    /// <summary>The game folder is not known, so a mod cannot be seen</summary>
    GameFolderUnknown,

    /// <summary>A mod was found, but the rows cannot be told from it (unknown style, unreadable file)</summary>
    Unknown,
}

/// <summary>How many rows a game's livery menu shows, and where that came from</summary>
/// <param name="Rows">The rows; null when they cannot be told</param>
/// <param name="Source">Where they came from</param>
/// <param name="ModWrittenUtc">
/// When the mod's file was written: a file newer than the running game is not loaded yet, so the menu is reopened
/// </param>
internal sealed record GridRows(int? Rows, GridRowsSource Source, DateTime? ModWrittenUtc = null);

/// <summary>
/// The grid of a game's livery menu (FH6: My Designs), for "Find in game": how many rows the menu shows (a menu mod
/// may change it) and where each livery of the save is in the grid
/// </summary>
/// <param name="DetectRows">The rows of the game installed in a folder (null: the folder is not known)</param>
/// <param name="Layout">The grid of a save folder for a number of rows</param>
/// <param name="DefaultRows">The rows of the game's own menu, used while the game folder is not known</param>
internal sealed record MyDesignsGrid(
    Func<string?, GridRows> DetectRows,
    Func<string, int, GridLayout> Layout,
    int DefaultRows);

/// <summary>The game's own cache of previews of liveries it only lists (FH6: the auction house)</summary>
/// <param name="FindDirectory">The cache folder on this PC; null when there is none</param>
/// <param name="IndexFileName">The file the game rewrites when the cache changes: the gallery rescans then</param>
internal sealed record ListingPreviewCache(Func<string?> FindDirectory, string IndexFileName);

/// <summary>
/// Everything the gallery knows about one game: what it is, where its saves and installation are, how a save keeps
/// liveries, and what the gallery can do there. The gallery takes it from the host instead of FH6 constants, so the
/// same windows can serve another game (a separate FH4 app, a second profile).
/// </summary>
internal sealed class GameProfile
{
    private readonly Lazy<Task<string?>> _installFolder;

    public GameProfile()
    {
        // Methods, not lambdas: the required Game is set after the constructor, and only these read it later
        _installFolder = new(FindInstallFolder);
        RunningGuard = new GameRunningGuard(CurrentGame);
    }

    /// <summary>The game in the toolkit's catalog: names, install and save hints, the window to send keys to</summary>
    public required ForzaGame Game { get; init; }

    /// <summary>The game's save folders on this PC</summary>
    public required PcGameSaveStore Saves { get; init; }

    /// <summary>How a save keeps liveries: for the scanner, the archive, backups and the windows</summary>
    public required ILiveryStore Liveries { get; init; }

    /// <summary>The names of the game's previews of a livery, the better one first (as <see cref="ILiveryStore.ListFiles"/> names them)</summary>
    public required IReadOnlyList<string> PreviewFileNames { get; init; }

    /// <summary>What the gallery can do in this game</summary>
    public required GameFeatures Features { get; init; }

    /// <summary>The My Designs grid for "Find in game"; null when the gallery cannot find liveries in this game</summary>
    public MyDesignsGrid? MyDesigns { get; init; }

    /// <summary>The cache of previews of listed liveries; null when the game keeps none</summary>
    public ListingPreviewCache? ListingPreviews { get; init; }

    /// <summary>
    /// How many liveries the save may hold: past it the game lets the player neither download nor make one. Auction
    /// liveries, which belong to a car, are not counted. Null when the game has no such limit or it is not known
    /// </summary>
    public int? MaxLiveries { get; init; }

    /// <summary>Asks before the save is changed while the game runs</summary>
    public GameRunningGuard RunningGuard { get; }

    /// <summary>True for a livery the game only lists (an auction livery), when the game has an auction house</summary>
    public bool IsAuction(string key) => Features.Auction && Liveries.IsExternalListing(key);

    /// <summary>The game's preview of a livery in the save, the better one first; null when there is none</summary>
    public string? FindPreview(string saveDataPath, string key)
    {
        var files = Liveries.ListFiles(saveDataPath, key);
        foreach (string name in PreviewFileNames)
            if (files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) is { Path: { } path }
                && File.Exists(path))
                return path;
        return null;
    }

    /// <summary>The installed game's folder (Steam first, then the Xbox app); looked up once, in the background</summary>
    public Task<string?> FindInstallFolderAsync() => _installFolder.Value;

    private Task<string?> FindInstallFolder() => Task.Run(() => GameInstallLocator.FindFirst(Game)?.Directory);

    private ForzaGame CurrentGame() => Game;

    /// <summary>Starts looking for the installed game, so the folder is known by the time it is needed</summary>
    public void WarmUpInstallFolder() => _ = _installFolder.Value;
}
