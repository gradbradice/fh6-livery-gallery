namespace LiveryGallery.Configuration;

internal static class AppSettings
{
    public const string Version = "1.0.0";

    public static string BaseCachePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FH6LiveryGallery");

    public static string ThumbsPath { get; } = Path.Combine(BaseCachePath, "thumbs");

    public static void CheckPaths()
    {
        if (!Directory.Exists(BaseCachePath)) Directory.CreateDirectory(BaseCachePath);
        if (!Directory.Exists(ThumbsPath)) Directory.CreateDirectory(ThumbsPath);
    }
}
