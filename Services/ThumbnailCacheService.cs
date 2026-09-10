using Avalonia.Media.Imaging;

namespace LiveryGallery.Services;

internal static class ThumbnailCacheService
{
    private static readonly Dictionary<string, (Bitmap Bitmap, int RefCount)> _cache = [];

    public static Bitmap? Acquire(string? thumbnailPath)
    {
        if (string.IsNullOrEmpty(thumbnailPath)) return null;

        if (_cache.TryGetValue(thumbnailPath, out var existing))
        {
            _cache[thumbnailPath] = (existing.Bitmap, existing.RefCount + 1);
            return existing.Bitmap;
        }

        Bitmap? bitmap = LoadBitmap(thumbnailPath);
        if (bitmap is null) return null;

        _cache[thumbnailPath] = (bitmap, 1);
        return bitmap;
    }

    public static void Release(string? thumbnailPath)
    {
        if (string.IsNullOrEmpty(thumbnailPath)) return;
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
