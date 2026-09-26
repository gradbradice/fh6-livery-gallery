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

    private sealed class Lease(string thumbnailPath)
    {
        public string ThumbnailPath { get; } = thumbnailPath;
        public LeaseState State = LeaseState.Pending;
        public PendingLoad? Pending;

        ~Lease()
        {
            if (State is LeaseState.Released or LeaseState.Empty) return;
            var lease = this;
            Dispatcher.UIThread.Post(() => ReleaseLease(lease), DispatcherPriority.Background);
        }
    }

    private const int RecentlyReleasedCapacity = 48;
    private const int MaxDecodeWidth = 400;

    private static readonly Dictionary<string, CacheEntry> _cache = [];
    private static readonly LinkedList<(string Path, Bitmap Bitmap)> _recentlyReleased = new();
    private static readonly Dictionary<string, LinkedListNode<(string Path, Bitmap Bitmap)>> _recentlyReleasedByPath = [];
    private static readonly Dictionary<string, PendingLoad> _inFlightLoads = [];
    private static readonly ConditionalWeakTable<Control, Lease> _leaseByControl = [];

    public static async Task<(bool WasSuperseded, Bitmap? Bitmap)> AcquireForAsync(Control control, string? thumbnailPath)
    {
        Dispatcher.UIThread.VerifyAccess();
        _leaseByControl.TryGetValue(control, out var previousLease);
        _leaseByControl.Remove(control);

        if (string.IsNullOrEmpty(thumbnailPath))
        {
            if (previousLease is not null) ReleaseLease(previousLease);
            return (false, null);
        }

        var lease = new Lease(thumbnailPath);
        _leaseByControl.AddOrUpdate(control, lease);

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
            return (false, null);
        }
    }

    public static void ReleaseFor(Control control)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_leaseByControl.TryGetValue(control, out var lease)) return;
        _leaseByControl.Remove(control);
        ReleaseLease(lease);
    }

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
        if (_recentlyReleasedByPath.Remove(thumbnailPath, out var stale))
        {
            _recentlyReleased.Remove(stale);
            if (!ReferenceEquals(stale.Value.Bitmap, bitmap)) DisposeLater(stale.Value.Bitmap);
        }

        _recentlyReleasedByPath[thumbnailPath] = _recentlyReleased.AddLast((thumbnailPath, bitmap));

        while (_recentlyReleased.Count > RecentlyReleasedCapacity && _recentlyReleased.First is { } oldest)
        {
            _recentlyReleased.RemoveFirst();
            _recentlyReleasedByPath.Remove(oldest.Value.Path);
            DisposeLater(oldest.Value.Bitmap);
        }
    }

    private static bool TryTakeRecentlyReleased(string thumbnailPath, out Bitmap bitmap)
    {
        if (_recentlyReleasedByPath.Remove(thumbnailPath, out var node))
        {
            _recentlyReleased.Remove(node);
            bitmap = node.Value.Bitmap;
            return true;
        }

        bitmap = null!;
        return false;
    }

    private static void DisposeLater(Bitmap bitmap) =>
        Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);

    private static async Task<LoadResult> LoadWhenStillNeededAsync(string path, PendingLoad pending)
    {
        await _decodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref pending.Reservations) <= 0) return new LoadResult(null, Skipped: true);
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
