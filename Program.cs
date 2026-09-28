using Avalonia;
using LiveryGallery.Configuration;
using LiveryGallery.Services;

namespace LiveryGallery;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppSettings.CheckPaths();
        RegisterLastChanceLogging();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void RegisterLastChanceLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                AppLogger.LogError($"Unhandled exception (terminating: {e.IsTerminating})", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.LogError("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}