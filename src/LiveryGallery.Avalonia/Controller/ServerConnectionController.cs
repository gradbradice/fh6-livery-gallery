using LiveryGallery.Services;
using LiveryGallery.ViewModels;
using Microsoft.Extensions.Logging;

namespace LiveryGallery.Controller;

/// <summary>
/// The gallery's connection to the livery server: <see cref="LiveryServerConnection"/> of ForzaToolkit.Liveries.Server
/// with the client the host gives (closed: ForzaToolkit.Crypto.Client) and the profile save of the current save folder
/// (<see cref="ProfileDataLocator"/>).
/// Created on the UI thread, which is where its timers and finished requests come back.
/// </summary>
internal sealed class ServerConnectionController(
    ILiveryServerClient service,
    ProfileDataLocator profileData,
    InstalledLiveriesCache cache,
    ServerStatusViewModel status,
    Func<string?> resolveSaveDataPath,
    Action<IReadOnlyDictionary<string, int>?> applyInstalled)
    : LiveryServerConnection(
        service, cache, status,
        () => profileData.FindProfileData(resolveSaveDataPath()),
        applyInstalled,
        logger: AppLogger.LoggerFactory.CreateLogger<LiveryServerConnection>());
