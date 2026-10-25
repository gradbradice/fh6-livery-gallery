using Avalonia.Threading;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Formats;
using ForzaToolkit.LiveryRender.Rendering;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Render;

/// <summary>
/// <see cref="ILiveryThumbnails"/> over LiveryRender: the preview is drawn by <see cref="LiveryRenderer.RenderThumbnailAsync"/>
/// with the game's preview camera and size. Shares the renderer with the 3D viewer (<see cref="LiveryRenderService"/>).
/// </summary>
internal sealed class LiveryThumbnails : ILiveryThumbnails
{
    // The renderer stays loaded this long after the last preview: scrolling asks for the next ones soon after
    private static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(10);

    // The game's preview camera and size, so a drawn preview sits among the game's ones
    private static readonly RenderOptions Options = new();

    private readonly LiveryRenderService _service;
    private readonly DispatcherTimer _idleTimer;
    private Task<LiveryRenderer?>? _acquiring;
    private LiveryRenderer? _renderer;
    private int _running;
    private bool _shuttingDown;

    public LiveryThumbnails(LiveryRenderService service)
    {
        _service = service;
        _idleTimer = new DispatcherTimer { Interval = IdleRelease };
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            if (_running == 0) ReleaseRenderer();
        };
        _service.StateChanged += OnServiceStateChanged;
    }

    public bool IsAvailable => !_shuttingDown && _service.IsAvailable;

    public async Task<byte[]?> DrawAsync(LiveryThumbnailRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dispatcher.UIThread.VerifyAccess();
        if (!IsAvailable || request.CarId <= 0) return null;

        _running++;
        _idleTimer.Stop();
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(request.LiveryFilePath, ct);
            var drawing = await Task.Run(() => LiveryDrawings.FromCLivery((uint)request.CarId, bytes), ct);
            if (!drawing.HasValue)
            {
                AppLog.LogWarning($"Preview from 3D: '{request.LiveryFilePath}' not parsed ({drawing.Error?.Code})");
                return null;
            }

            var renderer = await AcquireAsync();
            if (renderer is null || _shuttingDown) return null;
            var image = await renderer.RenderThumbnailAsync(drawing.Value!, Options, ct: ct);
            if (!image.HasValue)
            {
                AppLog.LogWarning($"Preview from 3D: car {request.CarId}, '{request.LiveryFilePath}': {image.Error}");
                return null;
            }
            return image.Value!.Png;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled(request.LiveryFilePath, $"Preview from 3D: failed to read '{request.LiveryFilePath}'", ex);
            return null;
        }
        finally
        {
            if (--_running == 0 && _renderer is not null && !_shuttingDown) _idleTimer.Start();
        }
    }

    private async Task<LiveryRenderer?> AcquireAsync()
    {
        if (_renderer is { IsDisposed: false } current) return current;
        if (_renderer is not null) ReleaseRenderer();

        var acquiring = _acquiring ??= AcquireCoreAsync();
        try
        {
            return await acquiring;
        }
        finally
        {
            if (ReferenceEquals(_acquiring, acquiring)) _acquiring = null;
        }
    }

    private async Task<LiveryRenderer?> AcquireCoreAsync()
    {
        var renderer = await _service.AcquireAsync();
        if (renderer is null) return null;
        if (_shuttingDown)
        {
            _service.Release();
            return null;
        }
        _renderer = renderer;
        return renderer;
    }

    private void ReleaseRenderer()
    {
        if (_renderer is null) return;
        _renderer = null;
        _service.Release();
    }

    // Another game folder: the service has dropped the renderer this one holds
    private void OnServiceStateChanged()
    {
        if (_renderer is not null && !ReferenceEquals(_renderer, _service.CurrentRenderer)) ReleaseRenderer();
    }

    /// <summary>The app is closing: no new previews; the renderer goes with the service</summary>
    public void BeginShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _idleTimer.Stop();
        _service.StateChanged -= OnServiceStateChanged;
        ReleaseRenderer();
    }
}
