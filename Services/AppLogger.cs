using LiveryGallery.Configuration;
using Serilog;
using Serilog.Core;

namespace LiveryGallery.Services;

internal static class AppLogger
{
    private static readonly Logger _logger = new LoggerConfiguration()
        .MinimumLevel.Warning()
        .WriteTo.File(
            Path.Combine(AppSettings.LogsPath, "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileTimeLimit: TimeSpan.FromDays(3),
            shared: true)
        .CreateLogger();

    private static volatile bool _isShutdown;

    public static void LogError(string context, Exception ex)
    {
        if (_isShutdown) return;
        try
        {
            _logger.Error(ex, "{Context}", context);
        }
        catch
        {
            
        }
    }

    private static readonly Dictionary<string, DateTime> _lastLoggedAt = [];
    private static readonly Lock _throttleLock = new();
    private static int _callsSinceCleanup;
    private static readonly TimeSpan _cleanupRetention = TimeSpan.FromDays(1);

    public static void LogErrorThrottled(string key, string context, Exception ex, TimeSpan? minInterval = null)
    {
        var interval = minInterval ?? TimeSpan.FromHours(1);
        bool shouldLog;
        lock (_throttleLock)
        {
            shouldLog = !_lastLoggedAt.TryGetValue(key, out var last) || DateTime.UtcNow - last >= interval;
            if (shouldLog) _lastLoggedAt[key] = DateTime.UtcNow;

            if (++_callsSinceCleanup >= 200)
            {
                _callsSinceCleanup = 0;
                var cutoff = DateTime.UtcNow - _cleanupRetention;
                var stale = _lastLoggedAt.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
                foreach (var staleKey in stale) _lastLoggedAt.Remove(staleKey);
            }
        }
        if (shouldLog) LogError(context, ex);
    }

    public static void Shutdown()
    {
        _isShutdown = true;
        _logger.Dispose();
    }
}
