namespace LiveryGallery.Services;

/// <summary>
/// Serialises every operation that changes the game's save folder (scan, archive, restore, backup).
/// Closed when the app exits, so nothing is cut off halfway. The mechanics live in ForzaToolkit.Core.
/// </summary>
internal sealed class SaveFolderLock : ClosableAsyncLock;
