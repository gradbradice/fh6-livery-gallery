using Avalonia.Controls;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Controller;

internal static class ThumbnailLifecycleController
{
    private static readonly ConditionalWeakTable<Control, IThumbnailHost> _hostByControl = new();
    private static readonly ConditionalWeakTable<IThumbnailHost, StrongBox<int>> _bindingCountByHost = new();

    public static async Task OnAttachedAsync(Control control)
    {
        if (control.DataContext is not IThumbnailHost host) return;
        await LoadIntoAsync(control, host);
    }

    public static void OnDetached(Control control)
    {
        Unbind(control);
        ThumbnailCacheService.ReleaseFor(control);
    }

    public static async Task OnDataContextChangedAsync(Control control)
    {
        if (control.DataContext is not IThumbnailHost host || TopLevel.GetTopLevel(control) is null)
        {
            Unbind(control);
            ThumbnailCacheService.ReleaseFor(control);
            return;
        }
        await LoadIntoAsync(control, host);
    }

    private static async Task LoadIntoAsync(Control control, IThumbnailHost host)
    {
        if (_hostByControl.TryGetValue(control, out var previous) && !ReferenceEquals(previous, host))
            Unbind(control);

        var (wasSuperseded, bitmap) = await ThumbnailCacheService.AcquireForAsync(control, host.ThumbnailPath);
        if (wasSuperseded) return;

        if (!ReferenceEquals(control.DataContext, host) || TopLevel.GetTopLevel(control) is null)
        {
            if (!_hostByControl.TryGetValue(control, out var bound) || !ReferenceEquals(bound, host))
                ThumbnailCacheService.ReleaseFor(control);
            return;
        }

        Bind(control, host);
        if (bitmap is not null || BindingCount(host) <= 1)
            host.Thumbnail = bitmap;
    }

    private static void Bind(Control control, IThumbnailHost host)
    {
        if (_hostByControl.TryGetValue(control, out var existing))
        {
            if (ReferenceEquals(existing, host)) return;
            Unbind(control);
        }

        _hostByControl.AddOrUpdate(control, host);
        _bindingCountByHost.GetValue(host, _ => new StrongBox<int>(0)).Value++;
    }

    private static void Unbind(Control control)
    {
        if (!_hostByControl.TryGetValue(control, out var host)) return;
        _hostByControl.Remove(control);

        if (_bindingCountByHost.TryGetValue(host, out var count) && --count.Value > 0) return;

        _bindingCountByHost.Remove(host);
        host.Thumbnail = null;
    }

    private static int BindingCount(IThumbnailHost host) =>
        _bindingCountByHost.TryGetValue(host, out var count) ? count.Value : 0;
}
