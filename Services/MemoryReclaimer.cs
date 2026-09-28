using System.Diagnostics;
using System.Runtime;

namespace LiveryGallery.Services;

internal static class MemoryReclaimer
{
    private static readonly TimeSpan MinFullCollectionInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinLohCompactionInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SlowCollectionThreshold = TimeSpan.FromMilliseconds(250);

    private static long _lastFullCollectionTicks = long.MinValue / 2;
    private static long _lastLohCompactionTicks = long.MinValue / 2;

    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable("FH6_LIVERY_GALLERY_NO_FORCED_GC") is not ("1" or "true");

    public static void RequestLohCompaction()
    {
        if (!IsEnabled || !TryEnterInterval(ref _lastLohCompactionTicks, MinLohCompactionInterval)) return;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    }

    private static readonly Lock _requestGate = new();
    private static bool _runScheduled; // a run is waiting or collecting
    private static bool _requestedAgain; // a release happened after the running collection took its snapshot
    private static (string Reason, Func<bool> StillWanted) _latestRequest;

    public static void ReclaimAfterLargeRelease(string reason, Func<bool> stillWanted)
    {
        if (!IsEnabled) return;
        lock (_requestGate)
        {
            _latestRequest = (reason, stillWanted);
            if (_runScheduled)
            {
                _requestedAgain = true;
                return;
            }
            _runScheduled = true;
        }
        _ = Task.Run(RunReclaimsAsync);
    }

    private static async Task RunReclaimsAsync()
    {
        while (true)
        {
            long waitMs = Volatile.Read(ref _lastFullCollectionTicks) + (long)MinFullCollectionInterval.TotalMilliseconds
                - Environment.TickCount64;
            if (waitMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(waitMs)).ConfigureAwait(false);

            (string Reason, Func<bool> StillWanted) request;
            lock (_requestGate)
            {
                _requestedAgain = false;
                request = _latestRequest;
            }

            Collect(request.Reason, request.StillWanted);

            lock (_requestGate)
            {
                if (!_requestedAgain)
                {
                    _runScheduled = false;
                    return;
                }
            }
        }
    }

    private static void Collect(string reason, Func<bool> stillWanted)
    {
        try
        {
            if (!stillWanted()) return;

            long before = GC.GetTotalMemory(forceFullCollection: false);
            var stopwatch = Stopwatch.StartNew();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            stopwatch.Stop();
            Volatile.Write(ref _lastFullCollectionTicks, Environment.TickCount64);

            if (stopwatch.Elapsed >= SlowCollectionThreshold)
            {
                long after = GC.GetTotalMemory(forceFullCollection: false);
                AppLogger.LogWarning(
                    $"Forced memory release ({reason}) took {stopwatch.ElapsedMilliseconds} ms " +
                    $"(managed heap {before / (1024 * 1024)} MB -> {after / (1024 * 1024)} MB)");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to release memory ({reason})", ex);
        }
    }

    private static bool TryEnterInterval(ref long lastTicks, TimeSpan minInterval)
    {
        long now = Environment.TickCount64;
        long last = Volatile.Read(ref lastTicks);
        if (now - last < (long)minInterval.TotalMilliseconds) return false;
        return Interlocked.CompareExchange(ref lastTicks, now, last) == last;
    }
}
