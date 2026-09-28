using System.Reflection;

namespace LiveryGallery.Configuration;

internal static class AppSettings
{
    public static string Version { get; } = ReadVersion();
    public static string UserAgent => $"FH6-Livery-Gallery/{Version}";

    private static string ReadVersion()
    {
        var assembly = typeof(AppSettings).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit>" (and possibly "-alpha"/"-beta")
            int cut = informational.IndexOfAny(['+', '-']);
            return cut > 0 ? informational[..cut] : informational;
        }
        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    public static string BaseCachePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FH6LiveryGallery");

    public static string ThumbsPath { get; } = Path.Combine(BaseCachePath, "thumbs");
    public static string LogsPath { get; } = Path.Combine(BaseCachePath, "logs");
    public static string ArchivePath { get; } = Path.Combine(BaseCachePath, ".archive");
    public static string BackupsPath { get; } = Path.Combine(BaseCachePath, ".backups");
    public static string BackupThumbsPath { get; } = Path.Combine(BaseCachePath, ".backup-thumbs");

    public static void CheckPaths()
    {
        if (!Directory.Exists(BaseCachePath)) Directory.CreateDirectory(BaseCachePath);
        if (!Directory.Exists(ThumbsPath)) Directory.CreateDirectory(ThumbsPath);
        if (!Directory.Exists(LogsPath)) Directory.CreateDirectory(LogsPath);
        if (!Directory.Exists(ArchivePath)) Directory.CreateDirectory(ArchivePath);
        if (!Directory.Exists(BackupsPath)) Directory.CreateDirectory(BackupsPath);
        if (!Directory.Exists(BackupThumbsPath)) Directory.CreateDirectory(BackupThumbsPath);
    }
}
