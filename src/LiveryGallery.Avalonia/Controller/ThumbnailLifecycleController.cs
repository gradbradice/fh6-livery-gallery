using Avalonia.Controls;
using Avalonia.Threading;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Controller;

internal static class ThumbnailLifecycleController
{
    private static readonly ConditionalWeakTable<Control, IThumbnailHost> _hostByControl = new();
    private static readonly ConditionalWeakTable<IThumbnailHost, StrongBox<int>> _bindingCountByHost = new();
    private static readonly TimeSpan OrphanSweepInterval = TimeSpan.FromSeconds(15);
    private static DispatcherTimer? _orphanSweepTimer;
    private static bool _shuttingDown;

    public static void BeginShutdown()
    {
        Dispatcher.UIThread.VerifyAccess();
        _shuttingDown = true;
        _orphanSweepTimer?.Stop();
        ThumbnailCacheService.BeginShutdown();
    }

    public static async Task OnAttachedAsync(Control control)
    {
        if (_shuttingDown) return;
        EnsureOrphanSweep();
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
        if (_shuttingDown) return;
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

    private static void EnsureOrphanSweep()
    {
        if (_orphanSweepTimer is not null || _shuttingDown) return;
        _orphanSweepTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = OrphanSweepInterval };
        _orphanSweepTimer.Tick += (_, _) => SweepOrphanedLeases();
        _orphanSweepTimer.Start();
    }

    private static void SweepOrphanedLeases()
    {
        if (_shuttingDown) return;
        try
        {
            var (released, collected) = ThumbnailCacheService.ReleaseOrphanedLeases(static (control, thumbnailPath) =>
                TopLevel.GetTopLevel(control) is not null
                && control.DataContext is IThumbnailHost host
                && string.Equals(host.ThumbnailPath, thumbnailPath, StringComparison.OrdinalIgnoreCase));

            foreach (var control in released)
            {
                Unbind(control);
                if (TopLevel.GetTopLevel(control) is not null && control.DataContext is IThumbnailHost current)
                    _ = ReloadSafelyAsync(control, current);
            }
            if (released.Count > 0 || collected > 0)
            {
                AppLogger.LogErrorThrottled("ThumbnailLifecycleController.Orphans",
                    $"Released {released.Count} preview(s) whose card missed its detach/data-context event " +
                    $"and {collected} preview(s) of cards that were garbage-collected without being detached",
                    new InvalidOperationException("Diagnostic: orphaned thumbnail leases"),
                    minInterval: TimeSpan.FromMinutes(10));
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to sweep orphaned preview leases", ex);
        }
    }

    private static async Task ReloadSafelyAsync(Control control, IThumbnailHost host)
    {
        try
        {
            await LoadIntoAsync(control, host);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to reload a preview after releasing an orphaned lease", ex);
        }
    }

    private static int BindingCount(IThumbnailHost host) =>
        _bindingCountByHost.TryGetValue(host, out var count) ? count.Value : 0;
}
