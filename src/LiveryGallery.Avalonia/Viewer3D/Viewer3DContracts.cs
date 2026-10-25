using Avalonia.Controls;
using ForzaToolkit.Games.Installation;

namespace LiveryGallery.Viewer3D;

// The 3D viewer and the game folder check as the gallery sees them. Nothing here refers to a closed package: the
// implementations live in the closed LiveryGallery.Render (on ForzaToolkit.LiveryRender and ForzaToolkit.Formats),
// and the host (LiveryGallery) hands them to the gallery.

// The game folder check's report is open: ForzaToolkit.Games.Installation (GameFolder, GameFolderReport,
// GameFolderFinding, GameFolderIssueCode). The closed side adds what needs its formats (can a car model be read).

/// <summary>Checks a game folder: can the 3D viewer work with it (the open <see cref="GameFolder.Check"/> and more)</summary>
internal interface IGameFolderCheck
{
    /// <summary>Checks <paramref name="path"/> off the UI thread; a failure of the check itself is a NotFound error</summary>
    Task<GameFolderReport> CheckAsync(string? path);
}

/// <summary>A livery to show in 3D</summary>
/// <param name="LiveryName">Its name, for the window title</param>
/// <param name="CarId">Its car</param>
/// <param name="LiveryFilePath">Its livery file (C_livery)</param>
internal sealed record Livery3DRequest(string LiveryName, int CarId, string LiveryFilePath);

/// <summary>Why a livery was not shown</summary>
internal enum Livery3DProblem
{
    None,

    /// <summary>The game folder is not usable (see <see cref="ILivery3DViewer.FolderReport"/>)</summary>
    NotAvailable,

    /// <summary>The livery file could not be read</summary>
    ReadFailed,

    /// <summary>The livery file could not be parsed; <see cref="Livery3DResult.Detail"/> names the parse error</summary>
    ParseFailed,
}

/// <summary>What <see cref="ILivery3DViewer.ShowAsync"/> did</summary>
internal readonly record struct Livery3DResult(Livery3DProblem Problem, string? Detail = null)
{
    public static Livery3DResult Shown => new(Livery3DProblem.None);
}

/// <summary>A livery to draw a preview of, from its car's 3D model</summary>
/// <param name="CarId">Its car</param>
/// <param name="LiveryFilePath">Its livery file (C_livery)</param>
internal sealed record LiveryThumbnailRequest(int CarId, string LiveryFilePath);

/// <summary>
/// Draws the preview of a livery whose save has none from the car's 3D model, with the game's preview camera and size.
/// Where it is kept is the gallery's business (<see cref="Services.RenderedThumbnails"/>). Used from the UI thread.
/// </summary>
internal interface ILiveryThumbnails
{
    /// <summary>Previews can be drawn: the game folder is usable</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The PNG of the livery's preview (seconds: the car is loaded). Null when it cannot be drawn: no game folder, the
    /// file is unreadable, the car is unknown.
    /// </summary>
    Task<byte[]?> DrawAsync(LiveryThumbnailRequest request, CancellationToken ct);
}

/// <summary>
/// The 3D viewer: knows the game folder (checked in the background), opens one viewer window and shows liveries in
/// it. Used from the UI thread.
/// </summary>
internal interface ILivery3DViewer
{
    /// <summary>The game folder is being checked</summary>
    bool IsChecking { get; }

    /// <summary>The game folder is usable</summary>
    bool IsAvailable { get; }

    /// <summary>The usable game folder; null when there is none</summary>
    string? GameDirectory { get; }

    /// <summary>The last check of the game folder; null before the first one finished</summary>
    GameFolderReport? FolderReport { get; }

    /// <summary>Checking started or finished, or the game folder changed</summary>
    event Action? StateChanged;

    /// <summary>Uses <paramref name="path"/> as the game folder (null: none); checks it first</summary>
    Task SetGamePathAsync(string? path);

    /// <summary>Shows the livery in the viewer window, opening it over <paramref name="owner"/> when needed</summary>
    Task<Livery3DResult> ShowAsync(Window owner, Livery3DRequest request);

    /// <summary>Previews drawn from the 3D model, with the same game folder</summary>
    ILiveryThumbnails Thumbnails { get; }

    /// <summary>The app is closing: closes the viewer window and opens no new one</summary>
    void BeginShutdown();

    /// <summary>Closes everything and waits up to <paramref name="timeout"/> for background work</summary>
    Task ShutdownAsync(TimeSpan timeout);
}

/// <summary>A package or library and its version, as the About dialog lists it</summary>
internal sealed record ComponentVersion(string Name, string Version);

/// <summary>Versions of the closed components (packages and the native library), for the About dialog</summary>
internal sealed record AboutComponents(IReadOnlyList<ComponentVersion> Closed)
{
    public static AboutComponents Unknown { get; } = new([]);
}
