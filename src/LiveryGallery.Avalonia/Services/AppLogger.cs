using LiveryGallery.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace LiveryGallery.Services;

internal static class AppLogger
{
    private static readonly Serilog.Core.Logger _logger = new LoggerConfiguration()
        .MinimumLevel.Warning()
        .WriteTo.File(
            Path.Combine(AppSettings.LogsPath, "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileTimeLimit: TimeSpan.FromDays(3),
            shared: true)
        .CreateLogger();

    private static volatile bool _isShutdown;

    /// <summary>
    /// The same log file as a <see cref="ILoggerFactory"/>, for the ForzaToolkit packages
    /// (<see cref="ForzaToolkitLogging.Initialize"/> at startup)
    /// </summary>
    public static ILoggerFactory LoggerFactory { get; } = new BridgeFactory();

    public static void LogError(string context, Exception ex)
    {
        if (_isShutdown) return;
        try
        {
            _logger.Error(ex, "{Context}", context);
        }
        catch
        {
            // The logger is the error channel itself
        }
    }

    public static void LogWarning(string context)
    {
        if (_isShutdown) return;
        try
        {
            _logger.Warning("{Context}", context);
        }
        catch
        {

        }
    }

    /// <summary>Shares its throttle with the ForzaToolkit packages: one key is logged once per interval, whoever logs it</summary>
    public static void LogErrorThrottled(string key, string context, Exception ex, TimeSpan? minInterval = null)
    {
        if (LogThrottle.Shared.ShouldLog(key, minInterval)) LogError(context, ex);
    }

    public static void Shutdown()
    {
        _isShutdown = true;
        _logger.Dispose();
    }

    private static void Write(LogLevel level, string category, Exception? exception, string message)
    {
        if (_isShutdown || level == LogLevel.None) return;
        var serilogLevel = level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal,
        };
        try
        {
            _logger.Write(serilogLevel, exception, "{Category}: {Context}", category, message);
        }
        catch
        {
            // The logger is the error channel itself
        }
    }

    private sealed class BridgeFactory : ILoggerFactory
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new BridgeLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }

    private sealed class BridgeLogger(string category) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Same threshold as the file: warnings and above
        public bool IsEnabled(LogLevel logLevel) => !_isShutdown && logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            Write(logLevel, category, exception, formatter(state, exception));
        }
    }
}
