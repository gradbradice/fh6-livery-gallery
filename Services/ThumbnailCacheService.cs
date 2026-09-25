using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Services;

internal static class ThumbnailCacheService
{
    private static readonly Dictionary<string, (Bitmap Bitmap, int RefCount)> _cache = [];
    private static readonly Dictionary<string, Task<Bitmap?>> _inFlightLoads = [];
    private static readonly ConditionalWeakTable<Control, Lease> _leaseByControl = [];

    private sealed class Lease(string thumbnailPath)
    {
        public string ThumbnailPath { get; } = thumbnailPath;
        private int _released;
        public bool TryMarkReleased() => Interlocked.Exchange(ref _released, 1) == 0;

        ~Lease()
        {
            if (!TryMarkReleased()) return;
            string path = ThumbnailPath;
            Dispatcher.UIThread.Post(() => Release(path), DispatcherPriority.Background);
        }
    }

    public static async Task<(bool WasSuperseded, Bitmap? Bitmap)> AcquireForAsync(Control control, string? thumbnailPath)
    {
        Dispatcher.UIThread.VerifyAccess();

        ReleaseFor(control);
        if (string.IsNullOrEmpty(thumbnailPath)) return (false, null);
        var lease = new Lease(thumbnailPath);
        _leaseByControl.AddOrUpdate(control, lease);

        if (_cache.TryGetValue(thumbnailPath, out var existing))
        {
            _cache[thumbnailPath] = (existing.Bitmap, existing.RefCount + 1);
            return (false, existing.Bitmap);
        }

        if (!_inFlightLoads.TryGetValue(thumbnailPath, out var loadTask))
        {
            loadTask = Task.Run(() => LoadBitmap(thumbnailPath));
            _inFlightLoads[thumbnailPath] = loadTask;
        }

        Bitmap? bitmap = await loadTask;
        if (_cache.TryGetValue(thumbnailPath, out var raced))
        {
            _cache[thumbnailPath] = (raced.Bitmap, raced.RefCount + 1);
            if (bitmap is not null && !ReferenceEquals(bitmap, raced.Bitmap))
                bitmap.Dispose();
            bitmap = raced.Bitmap;
        }
        else
        {
            _inFlightLoads.Remove(thumbnailPath);
            if (bitmap is not null)
                _cache[thumbnailPath] = (bitmap, 1);
        }

        bool stillCurrent = _leaseByControl.TryGetValue(control, out var currentLease)
            && ReferenceEquals(currentLease, lease);

        if (!stillCurrent)
        {
            if (bitmap is not null) Release(thumbnailPath);
            return (true, null);
        }

        return (false, bitmap);
    }

    public static void ReleaseFor(Control control)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_leaseByControl.TryGetValue(control, out var lease)) return;
        _leaseByControl.Remove(control);
        if (lease.TryMarkReleased()) Release(lease.ThumbnailPath);
    }

    private static void Release(string thumbnailPath)
    {
        if (!_cache.TryGetValue(thumbnailPath, out var existing)) return;

        if (existing.RefCount <= 1)
        {
            _cache.Remove(thumbnailPath);
            var bitmapToDispose = existing.Bitmap;
            Dispatcher.UIThread.Post(bitmapToDispose.Dispose, DispatcherPriority.Background);
        }
        else
        {
            _cache[thumbnailPath] = (existing.Bitmap, existing.RefCount - 1);
        }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 400, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(path, $"Failed to load preview '{path}'", ex);
            return null;
        }
    }
}
