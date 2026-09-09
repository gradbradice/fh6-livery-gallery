using Avalonia.Media.Imaging;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class ThumbnailCacheService
{
    private static readonly Dictionary<LiveryEntry, (Bitmap Bitmap, int RefCount)> _cache = [];

    public static Bitmap? Acquire(LiveryEntry entry, Func<Bitmap?> loader)
    {
        if (_cache.TryGetValue(entry, out var existing))
        {
            _cache[entry] = (existing.Bitmap, existing.RefCount + 1);
            return existing.Bitmap;
        }

        Bitmap? bitmap = loader();
        if (bitmap is null) return null;

        _cache[entry] = (bitmap, 1);
        return bitmap;
    }

    public static void Release(LiveryEntry entry)
    {
        if (!_cache.TryGetValue(entry, out var existing)) return;

        if (existing.RefCount <= 1)
        {
            _cache.Remove(entry);
            existing.Bitmap.Dispose();
        }
        else
        {
            _cache[entry] = (existing.Bitmap, existing.RefCount - 1);
        }
    }

    public static void ForceRemove(LiveryEntry entry)
    {
        if (_cache.Remove(entry, out var existing))
            existing.Bitmap.Dispose();
    }
}
