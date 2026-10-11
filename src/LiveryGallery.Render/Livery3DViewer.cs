using Avalonia.Controls;
using ForzaToolkit.Formats;
using ForzaToolkit.Games.Installation;
using ForzaToolkit.LiveryRender.Formats;
using ForzaToolkit.LiveryRender.Livery;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Viewer3D;
using LiveryGallery.Views;

namespace LiveryGallery.Render;

/// <summary>
/// <see cref="ILivery3DViewer"/> over LiveryRender: the renderer (<see cref="LiveryRenderService"/>), one viewer
/// window, and the livery file read through Formats (<see cref="LiveryDrawings"/> of ForzaToolkit.LiveryRender.Formats). Used from the UI thread.
/// </summary>
internal sealed class Livery3DViewer : ILivery3DViewer, IDisposable
{
    // A window closed while a car was loading keeps the renderer until the load ends, at most this long
    private static readonly TimeSpan LoadDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly LiveryRenderService _service = new();
    private readonly AppSettingsData _settings;
    private LiveryViewerWindow? _window;
    private Task _pendingRelease = Task.CompletedTask;
    private GameFolderReport? _report;
    private bool _shuttingDown;

    /// <param name="settings">The viewer window remembers its quality setting there</param>
    public Livery3DViewer(AppSettingsData settings)
    {
        _settings = settings;
        _service.StateChanged += OnServiceStateChanged;
    }

    public bool IsChecking => _service.IsChecking;
    public bool IsAvailable => _service.IsAvailable;
    public string? GameDirectory => _service.GameDirectory;
    public GameFolderReport? FolderReport => _report;

    public event Action? StateChanged;

    private void OnServiceStateChanged()
    {
        _report = _service.FolderCheck;
        StateChanged?.Invoke();
        // Another game folder: the open window still draws with the old renderer
        if (_window is not null && !_service.IsChecking && !ReferenceEquals(_window.Renderer, _service.CurrentRenderer))
            _window.Close();
    }

    public Task SetGamePathAsync(string? path) => _service.SetGamePathAsync(path);

    public async Task<Livery3DResult> ShowAsync(Window owner, Livery3DRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        if (_shuttingDown) return Livery3DResult.Shown;
        if (!_service.IsAvailable) return new Livery3DResult(Livery3DProblem.NotAvailable);

        ParseResult<LiveryDrawing> drawing;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(request.LiveryFilePath);
            drawing = await Task.Run(() => LiveryDrawings.FromCLivery((uint)Math.Max(0, request.CarId), bytes));
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"3D viewer: failed to read '{request.LiveryFilePath}'", ex);
            return new Livery3DResult(Livery3DProblem.ReadFailed);
        }

        if (drawing.Error is { } parseError)
            AppLogger.LogError($"3D viewer: '{request.LiveryFilePath}' parsed with status {drawing.Status}: {parseError.Message}",
                new InvalidDataException(parseError.Message));
        if (!drawing.HasValue)
            return new Livery3DResult(Livery3DProblem.ParseFailed, drawing.Error is { } e ? $"{e.Code} ({(int)e.Code})" : null);

        var acquired = _window is null ? await _service.AcquireAsync() : null;
        if (_shuttingDown)
        {
            if (acquired is not null) _service.Release();
            return Livery3DResult.Shown;
        }
        if (_window is null)
        {
            if (acquired is null) return new Livery3DResult(Livery3DProblem.NotAvailable);

            var window = new LiveryViewerWindow(acquired, _settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window)) _window = null;
                _pendingRelease = ReleaseAfterLoadsAsync(window, _pendingRelease);
            };
            _window = window;
            window.Show(owner);
        }
        else
        {
            if (acquired is not null) _service.Release();
            _window.Activate();
        }

        await _window.ShowLiveryAsync(request.LiveryName, drawing.Value!);
        return Livery3DResult.Shown;
    }

    private async Task ReleaseAfterLoadsAsync(LiveryViewerWindow window, Task previous)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            if (!await window.WaitForLoadsAsync(LoadDrainTimeout))
                AppLogger.LogWarning($"3D viewer: a load was still running {LoadDrainTimeout.TotalSeconds:0} s after the window closed; releasing the renderer anyway");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("3D viewer: waiting for the loads of a closed window failed", ex);
        }
        finally
        {
            _service.Release();
        }
    }

    public void BeginShutdown()
    {
        _shuttingDown = true;
        _service.StateChanged -= OnServiceStateChanged;
        _window?.Close();
    }

    /// <summary>The last resort when the app exits without <see cref="ShutdownAsync"/>: frees the renderer</summary>
    public void Dispose()
    {
        BeginShutdown();
        _service.Dispose();
    }

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        BeginShutdown();
        try
        {
            await _pendingRelease.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLogger.LogWarning($"3D viewer: the renderer was still in use {timeout.TotalSeconds:0.#} s after shutdown began");
        }
        await _service.ShutdownAsync(timeout);
    }
}
