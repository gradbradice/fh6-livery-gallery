using Avalonia.Controls;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Controller;

internal static class ThumbnailLifecycleController
{
    public static async Task OnAttachedAsync(Control control)
    {
        if (control.DataContext is not LiveryEntry entry) return;
        await LoadIntoAsync(control, entry);
    }

    public static void OnDetached(Control control)
    {
        if (control.DataContext is LiveryEntry entry) entry.Thumbnail = null;
        ThumbnailCacheService.ReleaseFor(control);
    }

    public static async Task OnDataContextChangedAsync(Control control)
    {
        if (control.DataContext is not LiveryEntry entry)
        {
            ThumbnailCacheService.ReleaseFor(control);
            return;
        }
        await LoadIntoAsync(control, entry);
    }

    private static async Task LoadIntoAsync(Control control, LiveryEntry entry)
    {
        var (wasSuperseded, bitmap) = await ThumbnailCacheService.AcquireForAsync(control, entry.ThumbnailPath);
        if (!wasSuperseded && ReferenceEquals(control.DataContext, entry))
            entry.Thumbnail = bitmap;
    }
}
