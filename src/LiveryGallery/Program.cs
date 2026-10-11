using Avalonia;
using LiveryGallery.Configuration;
using LiveryGallery.Services;

namespace LiveryGallery;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Who the program is (name, folders, User-Agent, repository): the gallery itself has no name of its own
        AppSettings.Initialize(ProgramIdentity.Fh6LiveryGallery);
        AppSettings.CheckPaths();
        ForzaToolkitLogging.Initialize(AppLogger.LoggerFactory);
        MemoryReclaimer.IsEnabled = Environment.GetEnvironmentVariable(AppSettings.NoForcedGcVariable) is not ("1" or "true");
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