using Avalonia;
using LiveryGallery.Configuration;

namespace LiveryGallery;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Who the program is (name, folders, User-Agent, repository): the gallery itself has no name of its own
        AppSettings.Initialize(ProgramIdentity.Fh6LiveryGallery);
        AppSettings.CheckPaths();
        // The log (also of the ForzaToolkit packages and of what nothing caught) and the User-Agent of every request:
        // ForzaToolkit.AppHost, the same as every ForzaToolkit app
        AppLog.Start(AppSettings.LogsPath);
        AppHttp.Configure(AppSettings.UserAgent);
        MemoryReclaimer.IsEnabled = Environment.GetEnvironmentVariable(AppSettings.NoForcedGcVariable) is not ("1" or "true");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}