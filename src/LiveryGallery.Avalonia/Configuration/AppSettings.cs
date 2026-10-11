using ForzaToolkit.Core;

namespace LiveryGallery.Configuration;

/// <summary>The program's version, folders and names, from the <see cref="AppIdentity"/> the host sets at start</summary>
internal static class AppSettings
{
    private static AppIdentity? _identity;

    /// <summary>Sets who the program is; the host calls it first thing, before any path is read</summary>
    public static void Initialize(AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
    }

    public static AppIdentity Identity => _identity
        ?? throw new InvalidOperationException("AppSettings.Initialize was not called: the host program sets its AppIdentity at start.");

    public static string Version { get; } = AppVersion.ReadDisplayVersion(typeof(AppSettings).Assembly);
    public static string UserAgent => $"{Identity.UserAgentProduct}/{Version}";

    /// <summary>Set to 1 or true to leave garbage collection to the runtime (see <see cref="MemoryReclaimer"/>)</summary>
    public static string NoForcedGcVariable => Identity.EnvironmentPrefix + "_NO_FORCED_GC";

    /// <summary>The program's data (%APPDATA%\&lt;folder&gt;)</summary>
    public static string BaseCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Identity.FolderName);

    /// <summary>The program's caches that stay on this PC (%LOCALAPPDATA%\&lt;folder&gt;)</summary>
    public static string LocalCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Identity.FolderName);

    public static string ThumbsPath => Path.Combine(BaseCachePath, "thumbs");
    public static string LogsPath => Path.Combine(BaseCachePath, "logs");
    public static string ArchivePath => Path.Combine(BaseCachePath, ".archive");
    public static string BackupsPath => Path.Combine(BaseCachePath, ".backups");
    public static string BackupThumbsPath => Path.Combine(BaseCachePath, ".backup-thumbs");

    /// <summary>Per-account data (the scan cache and what belongs to it), one folder per game account</summary>
    public static string AccountsPath => Path.Combine(BaseCachePath, "accounts");

    /// <summary>Previews of liveries removed while the gallery was closed, kept until the next launch</summary>
    public static string ChangesPreviewsPath => Path.Combine(BaseCachePath, ".changes-thumbs");

    public static void CheckPaths()
    {
        Directory.CreateDirectory(BaseCachePath);
        Directory.CreateDirectory(ThumbsPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(ArchivePath);
        Directory.CreateDirectory(BackupsPath);
        Directory.CreateDirectory(BackupThumbsPath);
    }
}
