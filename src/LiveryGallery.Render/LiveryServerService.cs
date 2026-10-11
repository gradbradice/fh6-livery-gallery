using ForzaToolkit.Crypto.Client;
using ForzaToolkit.Liveries.Server;
using LiveryGallery.Services;

namespace LiveryGallery.Render;

/// <summary>
/// The livery server through the closed ForzaToolkit.Crypto.Client, as the open <see cref="ILiveryServerClient"/>:
/// the client's status and errors become the toolkit's types. The profile save it sends is found by the open
/// <see cref="ProfileDataLocator"/>.
/// </summary>
internal sealed class LiveryServerService : ILiveryServerClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private readonly Lock _lock = new();
    private ForzaLiveryClient? _client;
    private bool _notConfigured;

    public bool IsAvailable
    {
        get
        {
            try { return ForzaLiveryServer.IsConfigured; }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("LiveryServer.IsConfigured", "Failed to read the built-in livery server address", ex);
                return false;
            }
        }
    }

    public static string UserAgent => Configuration.AppSettings.UserAgent;

    private ForzaLiveryClient? GetClient()
    {
        lock (_lock)
        {
            if (_client is not null || _notConfigured) return _client;
            try
            {
                var options = new ForzaLiveryClientOptions
                {
                    ProofOfWorkThreads = Math.Max(1, Environment.ProcessorCount / 2),
                };
                _client = ForzaLiveryClient.CreateForBuiltInServer(UserAgent, options, RequestTimeout);
            }
            catch (InvalidOperationException ex)
            {
                _notConfigured = true;
                AppLogger.LogError("The livery server client has no server address built in", ex);
            }
            return _client;
        }
    }

    public long MaxSaveBytes => ForzaLiveryClient.MaxFileBytes;

    public async Task<ServerCheckResult?> CheckConnectionAsync(CancellationToken ct)
    {
        if (GetClient() is not { } client) return null;
        var status = await client.CheckConnectionAsync(cancellationToken: ct).ConfigureAwait(false);
        return ToCheckResult(status);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetInstalledLiveriesAsync(ReadOnlyMemory<byte> profileData, CancellationToken ct)
    {
        var client = GetClient() ?? throw new InvalidOperationException("The livery server is not configured in this build");
        try
        {
            var liveries = await client.GetInstalledLiveriesAsync(profileData, cancellationToken: ct).ConfigureAwait(false);
            return CountByContainer(liveries);
        }
        catch (LiveryServiceException ex)
        {
            throw ToToolkitException(ex);
        }
    }

    internal static ServerCheckResult ToCheckResult(ServerConnectionStatus status) => new(
        ToState(status.State), status.Server?.Version, status.Server?.ApiVersion, status.Latency,
        status.ServerMessage, status.RetryAfter, status.RecheckAfter);

    internal static LiveryServerException ToToolkitException(LiveryServiceException ex) =>
        new(ToFailure(ex.Error), ex.Message, ex.StatusCode, ex.RetryAfter, ex);

    internal static ServerConnectionState ToState(ConnectionState state) => state switch
    {
        ConnectionState.Online => ServerConnectionState.Online,
        ConnectionState.Degraded => ServerConnectionState.Degraded,
        ConnectionState.Maintenance => ServerConnectionState.Maintenance,
        ConnectionState.Unreachable => ServerConnectionState.Unreachable,
        ConnectionState.RateLimited => ServerConnectionState.RateLimited,
        ConnectionState.UpdateRequired => ServerConnectionState.UpdateRequired,
        ConnectionState.AppRejected => ServerConnectionState.AppRejected,
        ConnectionState.CertificateError => ServerConnectionState.CertificateError,
        _ => ServerConnectionState.ServerError,
    };

    internal static ServerFailure ToFailure(LiveryServiceError error) => error switch
    {
        LiveryServiceError.InvalidSave => ServerFailure.InvalidSave,
        LiveryServiceError.FileTooLarge => ServerFailure.FileTooLarge,
        LiveryServiceError.SaveNotSupported => ServerFailure.SaveNotSupported,
        LiveryServiceError.RateLimited => ServerFailure.RateLimited,
        LiveryServiceError.AppRejected => ServerFailure.AppRejected,
        LiveryServiceError.AppOutdated => ServerFailure.AppOutdated,
        LiveryServiceError.ProofOfWorkFailed => ServerFailure.ProofOfWorkFailed,
        LiveryServiceError.ServerCertificateRejected => ServerFailure.ServerCertificateRejected,
        LiveryServiceError.ConnectionFailed => ServerFailure.ConnectionFailed,
        LiveryServiceError.Timeout => ServerFailure.Timeout,
        LiveryServiceError.InvalidResponse => ServerFailure.InvalidResponse,
        LiveryServiceError.Maintenance => ServerFailure.Maintenance,
        _ => ServerFailure.ServerError,
    };

    internal static Dictionary<string, int> CountByContainer(IEnumerable<InstalledLivery> liveries)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var livery in liveries)
        {
            if (!livery.HasLivery) continue;
            string name = Path.GetFileName(livery.LiveryFileName.Replace('\\', '/').TrimEnd('/'));
            if (name.Length == 0) continue;
            counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
        }
        return counts;
    }
}
