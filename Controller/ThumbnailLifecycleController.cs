using Avalonia.Controls;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Controller;

internal static class ThumbnailLifecycleController
{
    private static readonly ConditionalWeakTable<Control, LiveryEntry> _lastEntryByControl = new();

    public static async Task OnAttachedAsync(Control control)
    {
        if (control.DataContext is not LiveryEntry entry) return;
        await LoadIntoAsync(control, entry);
    }

    public static void OnDetached(Control control)
    {
        ClearPreviousEntry(control);
        if (control.DataContext is LiveryEntry entry) entry.Thumbnail = null;
        ThumbnailCacheService.ReleaseFor(control);
    }

    public static async Task OnDataContextChangedAsync(Control control)
    {
        ClearPreviousEntry(control);

        if (control.DataContext is not LiveryEntry entry)
        {
            ThumbnailCacheService.ReleaseFor(control);
            return;
        }
        await LoadIntoAsync(control, entry);
    }

    private static void ClearPreviousEntry(Control control)
    {
        if (_lastEntryByControl.TryGetValue(control, out var previous))
        {
            previous.Thumbnail = null;
            _lastEntryByControl.Remove(control);
        }
    }

    private static async Task LoadIntoAsync(Control control, LiveryEntry entry)
    {
        var (wasSuperseded, bitmap) = await ThumbnailCacheService.AcquireForAsync(control, entry.ThumbnailPath);
        if (!wasSuperseded && ReferenceEquals(control.DataContext, entry))
        {
            entry.Thumbnail = bitmap;
            _lastEntryByControl.AddOrUpdate(control, entry);
        }
    }
}
