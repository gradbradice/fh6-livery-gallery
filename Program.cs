using Avalonia;
using LiveryGallery.Configuration;

namespace LiveryGallery;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppSettings.CheckPaths();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}