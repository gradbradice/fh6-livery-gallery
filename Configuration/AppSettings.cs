namespace LiveryGallery.Configuration;

internal static class AppSettings
{
    public const string Version = "1.2.0";

    public static string BaseCachePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FH6LiveryGallery");

    public static string ThumbsPath { get; } = Path.Combine(BaseCachePath, "thumbs");
    public static string LogsPath { get; } = Path.Combine(BaseCachePath, "logs");
    public static string ArchivePath { get; } = Path.Combine(BaseCachePath, ".archive");
    public static string BackupsPath { get; } = Path.Combine(BaseCachePath, ".backups");

    public static void CheckPaths()
    {
        if (!Directory.Exists(BaseCachePath)) Directory.CreateDirectory(BaseCachePath);
        if (!Directory.Exists(ThumbsPath)) Directory.CreateDirectory(ThumbsPath);
        if (!Directory.Exists(LogsPath)) Directory.CreateDirectory(LogsPath);
        if (!Directory.Exists(ArchivePath)) Directory.CreateDirectory(ArchivePath);
        if (!Directory.Exists(BackupsPath)) Directory.CreateDirectory(BackupsPath);
    }
}
