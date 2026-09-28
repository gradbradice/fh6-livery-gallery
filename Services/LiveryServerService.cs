using ForzaToolkit.Crypto.Client;
using System.Globalization;
using System.Security.Cryptography;

namespace LiveryGallery.Services;

internal sealed class LiveryServerService
{
    public const string ProfileDataFileName = "C_ProfileData";
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

    public async Task<ServerConnectionStatus?> CheckConnectionAsync(CancellationToken ct)
    {
        if (GetClient() is not { } client) return null;
        return await client.CheckConnectionAsync(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetInstalledLiveriesAsync(ReadOnlyMemory<byte> profileData, CancellationToken ct)
    {
        var client = GetClient() ?? throw new InvalidOperationException("The livery server is not configured in this build");
        var liveries = await client.GetInstalledLiveriesAsync(profileData, cancellationToken: ct).ConfigureAwait(false);
        return CountByContainer(liveries);
    }

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

    public const string ProfileUserFolderPrefix = "User_";

    // saveDataPath = ...\u_<decUserId>_<gameId>\<containerId>\ContainersRoot
    // Profile lives in ContainersRoot\User_<hexUserId>\C_ProfileData
    public static string? FindProfileData(string? saveDataPath)
    {
        if (string.IsNullOrEmpty(saveDataPath) || !Directory.Exists(saveDataPath)) return null;
        try
        {
            if (ResolveUserId(saveDataPath) is not { } userId)
            {
                AppLogger.LogErrorThrottled($"{saveDataPath}|ProfileUserId",
                    $"Cannot determine the UserId for '{saveDataPath}', so {ProfileUserFolderPrefix}<hex> cannot be located",
                    new InvalidOperationException("ResolveSaveIdentity returned null"));
                return null;
            }

            string? userDir = FindUserFolder(saveDataPath, userId);
            if (userDir is null)
            {
                AppLogger.LogErrorThrottled($"{saveDataPath}|ProfileUserDir",
                    $"Folder '{ProfileUserFolderPrefix}{userId:X}' (UserId {userId}) was not found in '{saveDataPath}'",
                    new DirectoryNotFoundException(Path.Combine(saveDataPath, $"{ProfileUserFolderPrefix}{userId:X}")));
                return null;
            }

            var file = new FileInfo(Path.Combine(userDir, ProfileDataFileName));
            return file.Exists ? file.FullName : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(saveDataPath, $"Failed to look for {ProfileDataFileName} in '{saveDataPath}'", ex);
            return null;
        }
    }

    private static ulong? ResolveUserId(string saveDataPath)
    {
        var containerDir = Directory.GetParent(Path.TrimEndingDirectorySeparator(saveDataPath));
        var uFolder = containerDir?.Parent;
        if (containerDir is null || uFolder is null) return null;
        int? containerId = int.TryParse(containerDir.Name, out int id) ? id : null;
        return LocalSaveService.ResolveSaveIdentity(uFolder.FullName, containerId)?.UserId;
    }

    private static string? FindUserFolder(string containersRoot, ulong userId)
    {
        string exact = Path.Combine(containersRoot, $"{ProfileUserFolderPrefix}{userId:X}");
        if (Directory.Exists(exact)) return exact;

        foreach (string dir in Directory.EnumerateDirectories(containersRoot, ProfileUserFolderPrefix + "*"))
        {
            string hex = Path.GetFileName(dir)[ProfileUserFolderPrefix.Length..];
            if (ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                && value == userId)
                return dir;
        }
        return null;
    }

    public static async Task<(byte[] Data, string Hash)> ReadProfileDataAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920, useAsync: true);
        if (stream.Length > ForzaLiveryClient.MaxFileBytes)
            throw new LiveryServiceException(LiveryServiceError.FileTooLarge, "The save file is too large", null, null, null);
        using var buffer = new MemoryStream((int)stream.Length);
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        byte[] data = buffer.ToArray();
        return (data, Convert.ToHexStringLower(SHA256.HashData(data)));
    }
}
