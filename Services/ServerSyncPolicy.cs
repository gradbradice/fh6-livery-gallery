using ForzaToolkit.Crypto.Client;
using LiveryGallery.Enums;
using System.Net;

namespace LiveryGallery.Services;

internal static class ServerSyncPolicy
{
    public static readonly TimeSpan MinUploadInterval = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan SyncDebounce = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FocusRecheckAfter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ManualRecheckMinInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan[] UnreachableBackoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

    public static ServerIndicatorState ToIndicator(ConnectionState state, bool networkAvailable) => state switch
    {
        ConnectionState.Online => ServerIndicatorState.Online,
        ConnectionState.Degraded => ServerIndicatorState.Degraded,
        ConnectionState.Maintenance => ServerIndicatorState.Maintenance,
        ConnectionState.Unreachable => networkAvailable ? ServerIndicatorState.Unreachable : ServerIndicatorState.NoInternet,
        ConnectionState.RateLimited => ServerIndicatorState.RateLimited,
        ConnectionState.UpdateRequired => ServerIndicatorState.UpdateRequired,
        ConnectionState.AppRejected => ServerIndicatorState.AppRejected,
        ConnectionState.CertificateError => ServerIndicatorState.CertificateError,
        _ => ServerIndicatorState.ServerError,
    };

    public static TimeSpan? NextCheckDelay(ServerConnectionStatus status, int consecutiveUnreachable)
    {
        if (status.RecheckAfter is not { } delay) return null;
        if (status.State != ConnectionState.Unreachable) return delay;
        int step = Math.Clamp(consecutiveUnreachable - 1, 0, UnreachableBackoff.Length - 1);
        return UnreachableBackoff[step];
    }

    public static ConnectionState? ConnectionStateFor(LiveryServiceException error) => error.Error switch
    {
        LiveryServiceError.Maintenance => ConnectionState.Maintenance,
        LiveryServiceError.RateLimited => ConnectionState.RateLimited,
        LiveryServiceError.AppOutdated => ConnectionState.UpdateRequired,
        LiveryServiceError.AppRejected => ConnectionState.AppRejected,
        LiveryServiceError.ServerCertificateRejected => ConnectionState.CertificateError,
        LiveryServiceError.ConnectionFailed or LiveryServiceError.Timeout => ConnectionState.Unreachable,
        LiveryServiceError.ServerError when error.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout => ConnectionState.Unreachable,
        LiveryServiceError.ServerError => ConnectionState.ServerError,
        _ => null, // a problem with this particular upload, not with the connection
    };

    public static (InstalledSyncState Sync, TimeSpan? RetryAfter, bool RejectFile) ForUploadError(LiveryServiceException error) => error.Error switch
    {
        LiveryServiceError.InvalidSave or LiveryServiceError.FileTooLarge => (InstalledSyncState.SaveRejected, null, true),
        // The game changed its keys. The server needs an update. Try again now and then, not on every save
        LiveryServiceError.SaveNotSupported => (InstalledSyncState.SaveNotSupported, TimeSpan.FromMinutes(30), false),
        LiveryServiceError.RateLimited => (InstalledSyncState.Failed, ClampRetry(error.RetryAfter, TimeSpan.FromMinutes(1)), false),
        LiveryServiceError.Maintenance => (InstalledSyncState.Failed, ClampRetry(error.RetryAfter, TimeSpan.FromMinutes(2)), false),
        LiveryServiceError.ProofOfWorkFailed or LiveryServiceError.InvalidResponse => (InstalledSyncState.Failed, TimeSpan.FromMinutes(5), false),
        LiveryServiceError.ConnectionFailed or LiveryServiceError.Timeout => (InstalledSyncState.Failed, TimeSpan.FromSeconds(30), false),
        LiveryServiceError.ServerError => (InstalledSyncState.Failed, TimeSpan.FromMinutes(1), false),
        // Update needed, build rejected, certificate problem. Nothing to retry until the user acts
        _ => (InstalledSyncState.Failed, null, false),
    };

    private static TimeSpan ClampRetry(TimeSpan? value, TimeSpan fallback) =>
        value is { } v && v > TimeSpan.Zero
            ? (v < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : v > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : v)
            : fallback;
}
