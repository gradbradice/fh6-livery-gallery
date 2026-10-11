using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Services;

internal static class ThumbnailCacheService
{
    private sealed class CacheEntry(Bitmap bitmap)
    {
        public Bitmap Bitmap { get; } = bitmap;
        public int RefCount;
    }

    private sealed class PendingLoad
    {
        public Task<LoadResult> Task { get; set; } = null!;
        public int Reservations;
        public bool Published;
        public bool HasBitmap;
    }

    private readonly record struct LoadResult(Bitmap? Bitmap, bool Skipped);
    private static readonly SemaphoreSlim _decodeGate = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));

    private enum LeaseState { Pending, Holding, Empty, Released }

    private sealed class Lease(string thumbnailPath, Control control)
    {
        public string ThumbnailPath { get; } = thumbnailPath;
        public WeakReference<Control> Owner { get; } = new(control);
        public LeaseState State = LeaseState.Pending;
        public PendingLoad? Pending;
    }

    private readonly record struct RecentBitmap(string Path, Bitmap Bitmap, long Bytes);
    private const int RecentlyReleasedCapacity = 48;
    private const long RecentlyReleasedMaxBytes = 32L * 1024 * 1024;
    private const int UrgentDisposalThreshold = 16;
    private static readonly List<Bitmap> _pendingDisposal = [];
    private static bool _disposalScheduled;
    private const int MaxDecodeWidth = 400;

    private static readonly Dictionary<string, CacheEntry> _cache = [];
    private static readonly LinkedList<RecentBitmap> _recentlyReleased = new();
    private static readonly Dictionary<string, LinkedListNode<RecentBitmap>> _recentlyReleasedByPath = [];
    private static long _recentlyReleasedBytes;
    private static readonly Dictionary<string, PendingLoad> _inFlightLoads = [];
    private static readonly ConditionalWeakTable<Control, Lease> _leaseByControl = [];
    private static readonly HashSet<Lease> _activeLeases = [];
    private static volatile bool _shuttingDown;

    public readonly record struct Statistics(
        int CachedBitmaps, int ActiveLeases, int InFlightLoads, int RecentlyReleased, long RecentlyReleasedBytes);

    public static Statistics GetStatistics()
    {
        Dispatcher.UIThread.VerifyAccess();
        return new Statistics(_cache.Count, _activeLeases.Count, _inFlightLoads.Count, _recentlyReleased.Count, _recentlyReleasedBytes);
    }

    public static async Task<(bool WasSuperseded, Bitmap? Bitmap)> AcquireForAsync(Control control, string? thumbnailPath)
    {
        Dispatcher.UIThread.VerifyAccess();
        _leaseByControl.TryGetValue(control, out var previousLease);
        _leaseByControl.Remove(control);

        if (string.IsNullOrEmpty(thumbnailPath) || _shuttingDown)
        {
            if (previousLease is not null) ReleaseLease(previousLease);
            return (false, null);
        }

        var lease = new Lease(thumbnailPath, control);
        _leaseByControl.AddOrUpdate(control, lease);
        _activeLeases.Add(lease);

        while (true)
        {
            if (!_cache.TryGetValue(thumbnailPath, out var cached) && TryTakeRecentlyReleased(thumbnailPath, out var recent))
            {
                cached = new CacheEntry(recent);
                _cache[thumbnailPath] = cached;
            }

            if (cached is not null)
            {
                cached.RefCount++;
                lease.Pending = null;
                lease.State = LeaseState.Holding;
                if (previousLease is not null) ReleaseLease(previousLease);
                return (false, cached.Bitmap);
            }

            if (!_inFlightLoads.TryGetValue(thumbnailPath, out var pending))
            {
                pending = new PendingLoad { Reservations = 1 };
                _inFlightLoads[thumbnailPath] = pending;
                var newPending = pending;
                pending.Task = Task.Run(() => LoadWhenStillNeededAsync(thumbnailPath, newPending));
            }
            else
            {
                pending.Reservations++;
            }
            lease.Pending = pending;
            lease.State = LeaseState.Pending;
            if (previousLease is not null)
            {
                ReleaseLease(previousLease);
                previousLease = null;
            }

            LoadResult result;
            try
            {
                result = await pending.Task;
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled(thumbnailPath, $"Failed to load preview '{thumbnailPath}'", ex);
                result = new LoadResult(null, Skipped: false);
            }

            Publish(thumbnailPath, pending, result.Bitmap);
            if (lease.State == LeaseState.Released) return (true, null);
            lease.Pending = null;
            if (result.Skipped) continue;

            if (pending.HasBitmap && _cache.TryGetValue(thumbnailPath, out var entry))
            {
                lease.State = LeaseState.Holding;
                return (false, entry.Bitmap);
            }

            lease.State = LeaseState.Empty;
            _activeLeases.Remove(lease);
            return (false, null);
        }
    }

    public static (List<Control> Released, int Collected) ReleaseOrphanedLeases(Func<Control, string, bool> isStillShowing)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_activeLeases.Count == 0) return ([], 0);

        List<Control> released = [];
        int collected = 0;
        foreach (var lease in _activeLeases.ToList())
        {
            if (!lease.Owner.TryGetTarget(out var control))
            {
                ReleaseLease(lease);
                collected++;
                continue;
            }

            if (isStillShowing(control, lease.ThumbnailPath)) continue;
            if (_leaseByControl.TryGetValue(control, out var mapped) && ReferenceEquals(mapped, lease))
                _leaseByControl.Remove(control);
            ReleaseLease(lease);
            released.Add(control);
        }
        return (released, collected);
    }

    public static void ReleaseFor(Control control)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_leaseByControl.TryGetValue(control, out var lease)) return;
        _leaseByControl.Remove(control);
        ReleaseLease(lease);
    }

    public static void BeginShutdown() => _shuttingDown = true;

    private static void Publish(string thumbnailPath, PendingLoad pending, Bitmap? loaded)
    {
        if (pending.Published) return;
        pending.Published = true;

        if (_inFlightLoads.TryGetValue(thumbnailPath, out var current) && ReferenceEquals(current, pending))
            _inFlightLoads.Remove(thumbnailPath);

        if (loaded is null) return;

        if (pending.Reservations <= 0)
        {
            if (_cache.ContainsKey(thumbnailPath)) loaded.Dispose();
            else AddRecentlyReleased(thumbnailPath, loaded);
            return;
        }

        pending.HasBitmap = true;
        if (_cache.TryGetValue(thumbnailPath, out var existing))
        {
            existing.RefCount += pending.Reservations;
            if (!ReferenceEquals(existing.Bitmap, loaded)) loaded.Dispose();
        }
        else
        {
            _cache[thumbnailPath] = new CacheEntry(loaded) { RefCount = pending.Reservations };
        }
    }

    private static void ReleaseLease(Lease lease)
    {
        switch (lease.State)
        {
            case LeaseState.Holding:
                Release(lease.ThumbnailPath);
                break;

            case LeaseState.Pending when lease.Pending is { } pending:
                if (!pending.Published) pending.Reservations--;
                else if (pending.HasBitmap) Release(lease.ThumbnailPath);
                break;
        }

        lease.State = LeaseState.Released;
        lease.Pending = null;
        _activeLeases.Remove(lease);
    }

    private static void Release(string thumbnailPath)
    {
        if (!_cache.TryGetValue(thumbnailPath, out var existing)) return;

        if (--existing.RefCount <= 0)
        {
            _cache.Remove(thumbnailPath);
            AddRecentlyReleased(thumbnailPath, existing.Bitmap);
        }
    }

    private static void AddRecentlyReleased(string thumbnailPath, Bitmap bitmap)
    {
        if (_recentlyReleasedByPath.TryGetValue(thumbnailPath, out var stale))
            RemoveRecent(stale, dispose: !ReferenceEquals(stale.Value.Bitmap, bitmap));

        var recent = new RecentBitmap(thumbnailPath, bitmap, EstimateBytes(bitmap));
        _recentlyReleasedByPath[thumbnailPath] = _recentlyReleased.AddLast(recent);
        _recentlyReleasedBytes += recent.Bytes;

        while ((_recentlyReleased.Count > RecentlyReleasedCapacity || _recentlyReleasedBytes > RecentlyReleasedMaxBytes)
            && _recentlyReleased.First is { } oldest)
        {
            RemoveRecent(oldest, dispose: true);
        }
    }

    private static bool TryTakeRecentlyReleased(string thumbnailPath, out Bitmap bitmap)
    {
        if (_recentlyReleasedByPath.TryGetValue(thumbnailPath, out var node))
        {
            bitmap = node.Value.Bitmap;
            RemoveRecent(node, dispose: false);
            return true;
        }

        bitmap = null!;
        return false;
    }

    private static void RemoveRecent(LinkedListNode<RecentBitmap> node, bool dispose)
    {
        _recentlyReleased.Remove(node);
        _recentlyReleasedByPath.Remove(node.Value.Path);
        _recentlyReleasedBytes -= node.Value.Bytes;
        if (dispose) DisposeLater(node.Value.Bitmap);
    }

    private static long EstimateBytes(Bitmap bitmap)
    {
        try
        {
            var size = bitmap.PixelSize;
            return (long)size.Width * size.Height * 4; // decoded as 32 bpp
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled("ThumbnailCacheService.EstimateBytes", "Failed to read the size of a preview bitmap", ex);
            return (long)MaxDecodeWidth * MaxDecodeWidth * 4;
        }
    }

    private static void DisposeLater(Bitmap bitmap)
    {
        _pendingDisposal.Add(bitmap);
        if (!_disposalScheduled)
        {
            _disposalScheduled = true;
            Dispatcher.UIThread.Post(FlushPendingDisposals, DispatcherPriority.Background);
        }
        else if (_pendingDisposal.Count == UrgentDisposalThreshold)
        {
            Dispatcher.UIThread.Post(FlushPendingDisposals, DispatcherPriority.Normal);
        }
    }

    private static void FlushPendingDisposals()
    {
        _disposalScheduled = false;
        if (_pendingDisposal.Count == 0) return;

        var batch = _pendingDisposal.ToArray();
        _pendingDisposal.Clear();
        foreach (var bitmap in batch)
        {
            try
            {
                bitmap.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("ThumbnailCacheService.Dispose", "Failed to dispose a preview bitmap", ex);
            }
        }
    }

    private static async Task<LoadResult> LoadWhenStillNeededAsync(string path, PendingLoad pending)
    {
        await _decodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref pending.Reservations) <= 0) return new LoadResult(null, Skipped: true);
            if (_shuttingDown) return new LoadResult(null, Skipped: false);
            return new LoadResult(LoadBitmap(path), Skipped: false);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            Bitmap bitmap;
            using (var stream = File.OpenRead(path))
                bitmap = new Bitmap(stream);
            if (bitmap.PixelSize.Width <= MaxDecodeWidth) return bitmap;

            bitmap.Dispose();
            using var resizeStream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(resizeStream, MaxDecodeWidth, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(path, $"Failed to load preview '{path}'", ex);
            return null;
        }
    }
}
