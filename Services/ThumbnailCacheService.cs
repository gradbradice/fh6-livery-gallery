using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace LiveryGallery.Services;
internal static class ThumbnailCacheService
{
    private static readonly Dictionary<string, (Bitmap Bitmap, int RefCount)> _cache = [];
    private static readonly Dictionary<Control, string> _acquiredByControl = [];
    private static readonly Dictionary<string, Task<Bitmap?>> _inFlightLoads = [];

    public static async Task<Bitmap?> AcquireForAsync(Control control, string? thumbnailPath)
    {
        ReleaseFor(control);
        if (string.IsNullOrEmpty(thumbnailPath)) return null;

        if (_cache.TryGetValue(thumbnailPath, out var existing))
        {
            _cache[thumbnailPath] = (existing.Bitmap, existing.RefCount + 1);
            _acquiredByControl[control] = thumbnailPath;
            return existing.Bitmap;
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

        if (!_acquiredByControl.TryGetValue(control, out var currentPath) || currentPath != thumbnailPath)
        {
            if (bitmap is not null) Release(thumbnailPath);
            return null;
        }

        return bitmap;
    }

    public static void ReleaseFor(Control control)
    {
        if (_acquiredByControl.Remove(control, out var previousPath))
            Release(previousPath);
    }

    private static void Release(string thumbnailPath)
    {
        if (!_cache.TryGetValue(thumbnailPath, out var existing)) return;

        if (existing.RefCount <= 1)
        {
            _cache.Remove(thumbnailPath);
            existing.Bitmap.Dispose();
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
