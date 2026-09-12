using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Diagnostics;

namespace LiveryGallery.Services;

internal static class ThumbnailCacheService
{
    private static readonly Dictionary<string, (Bitmap Bitmap, int RefCount)> _cache = [];
    private static readonly Dictionary<Control, string> _acquiredByControl = [];
    private static readonly Dictionary<string, Task<Bitmap?>> _inFlightLoads = [];
    private static readonly Dictionary<Control, long> _acquisitionToken = [];
    private static long _nextToken;

    public static async Task<(bool WasSuperseded, Bitmap? Bitmap)> AcquireForAsync(Control control, string? thumbnailPath)
    {
        ReleaseFor(control);
        if (string.IsNullOrEmpty(thumbnailPath)) return (false, null);

        long myToken = ++_nextToken;
        _acquisitionToken[control] = myToken;

        if (_cache.TryGetValue(thumbnailPath, out var existing))
        {
            _cache[thumbnailPath] = (existing.Bitmap, existing.RefCount + 1);
            _acquiredByControl[control] = thumbnailPath;
            return (false, existing.Bitmap);
        }

        _acquiredByControl[control] = thumbnailPath;

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

        bool stillCurrent = _acquiredByControl.TryGetValue(control, out var currentPath)
            && currentPath == thumbnailPath
            && _acquisitionToken.TryGetValue(control, out var currentToken)
            && currentToken == myToken;

        if (!stillCurrent)
        {
            if (bitmap is not null) Release(thumbnailPath);
            return (true, null);
        }

        return (false, bitmap);
    }

    public static void ReleaseFor(Control control)
    {
        _acquisitionToken.Remove(control);
        if (_acquiredByControl.Remove(control, out var previousPath))
            Release(previousPath);
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
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled(path, $"Failed to load preview '{path}'", ex);
            return null;
        }
    }
}
