using Avalonia.Threading;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Assets;

namespace LiveryGallery.Services;

internal sealed class LiveryRenderService : IDisposable
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FH6LiveryGallery", "render-cache");

    private static readonly TimeSpan MemoryReleaseDelay = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _memoryReleaseTimer = new() { Interval = MemoryReleaseDelay };
    private Task _pendingPathChange = Task.CompletedTask;
    private Task _carIndexPreparation = Task.CompletedTask;
    private volatile string? _preparationTarget;

    private int _version;
    private int _users;
    private string? _currentPath;
    private LiveryRenderer? _renderer;
    private volatile bool _hasRenderer;
    private bool _disposed;

    public GameFolderCheck? FolderCheck { get; private set; }
    public bool IsChecking { get; private set; } = true;
    public string? GameDirectory => FolderCheck is { IsValid: true, GameDirectory: { } root } ? root : null;
    public bool IsAvailable => !IsChecking && GameDirectory is not null;
    public LiveryRenderer? CurrentRenderer => _renderer;

    public event Action? StateChanged;

    public LiveryRenderService()
    {
        _memoryReleaseTimer.Tick += (_, _) =>
        {
            _memoryReleaseTimer.Stop();
            if (_renderer is null) RequestMemoryRelease();
        };
    }

    public async Task<LiveryRenderer?> AcquireAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        while (!_disposed)
        {
            var pathChange = _pendingPathChange;
            var preparation = _carIndexPreparation;
            if (pathChange.IsCompleted && preparation.IsCompleted) break;
            await Task.WhenAll(pathChange, preparation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext
                | ConfigureAwaitOptions.SuppressThrowing);
        }

        if (_disposed || IsChecking || GameDirectory is not { } root) return null;
        _memoryReleaseTimer.Stop();
        _users++;
        if (_renderer is null)
        {
            _renderer = new LiveryRenderer(new LiveryRendererOptions
            {
                GameDirectory = root,
                CacheDirectory = CacheDirectory,
                MaxCachedCars = 1,
            });
            _hasRenderer = true;
        }
        return _renderer;
    }

    public void Release()
    {
        if (_users > 0) _users--;
        if (_users == 0 && _renderer is not null)
            Dispatcher.UIThread.Post(ReleaseIfUnused, DispatcherPriority.Background);
    }

    private void ReleaseIfUnused()
    {
        if (_users == 0) ReleaseRenderer();
    }

    public Task SetGamePathAsync(string? path)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return Task.CompletedTask;
        if (!IsChecking && FolderCheck is not null && string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var change = SetGamePathCoreAsync(path, ++_version);
        _pendingPathChange = change;
        return change;
    }

    private async Task SetGamePathCoreAsync(string? path, int version)
    {
        IsChecking = true;
        StateChanged?.Invoke();

        GameFolderCheck check;
        try
        {
            check = await Task.Run(() => GameFolder.Check(path));
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Game folder check failed for '{path}'", ex);
            check = new GameFolderCheck
            {
                InputPath = path,
                Issues = [new GameFolderIssue(GameFolderProblem.NotFound, true, ex.Message, path)],
            };
        }

        if (version != _version || _disposed) return;

        string? previousRoot = GameDirectory;
        FolderCheck = check;
        _currentPath = path;
        IsChecking = false;

        foreach (var issue in check.Issues)
            AppLogger.LogError($"Game folder '{path}': {issue.Code} ({(issue.IsError ? "error" : "warning")}): {issue.Message} {issue.Path}",
                new InvalidOperationException(issue.Code.ToString()));

        string? root = GameDirectory;
        bool rootChanged = !string.Equals(previousRoot, root, StringComparison.OrdinalIgnoreCase);
        var obsolete = rootChanged ? _renderer : null;
        if (obsolete is not null) ClearRenderer();
        if (rootChanged) _preparationTarget = root;
        StateChanged?.Invoke();
        if (obsolete is not null) DisposeRenderer(obsolete);
        if (root is not null && rootChanged) StartCarIndexPreparation(root);
    }

    private void StartCarIndexPreparation(string root)
    {
        var previousPreparation = _carIndexPreparation;
        _carIndexPreparation = Task.Run(async () =>
        {
            await previousPreparation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!string.Equals(_preparationTarget, root, StringComparison.OrdinalIgnoreCase)) return;
            PrepareCarIndex(root);
        });
        _ = _carIndexPreparation.ContinueWith(
            _ => Dispatcher.UIThread.Post(ScheduleMemoryRelease),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static void PrepareCarIndex(string root)
    {
        try
        {
            using var renderer = new LiveryRenderer(new LiveryRendererOptions
            {
                GameDirectory = root,
                CacheDirectory = CacheDirectory,
            });
            renderer.Cars?.Refresh();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to prepare the car index for the livery renderer", ex);
        }
    }

    private void ClearRenderer()
    {
        _renderer = null;
        _hasRenderer = false;
    }

    private void ReleaseRenderer()
    {
        var renderer = _renderer;
        ClearRenderer();
        if (renderer is not null) DisposeRenderer(renderer);
    }

    private void DisposeRenderer(LiveryRenderer renderer)
    {
        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to dispose the livery renderer", ex);
        }
        ScheduleMemoryRelease();
    }

    private void ScheduleMemoryRelease()
    {
        if (_disposed) return;
        _memoryReleaseTimer.Stop();
        _memoryReleaseTimer.Start();
    }

    private void RequestMemoryRelease() =>
        MemoryReclaimer.ReclaimAfterLargeRelease("3D renderer released", () => !_hasRenderer && !_disposed);

    public async Task ShutdownAsync(TimeSpan timeout)
    {
        Dispose();
        try
        {
            await _carIndexPreparation.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLogger.LogWarning($"The car index preparation was still running {timeout.TotalSeconds:0.#} s after shutdown began");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _version++;
        _preparationTarget = null;
        _memoryReleaseTimer.Stop();
        var renderer = _renderer;
        ClearRenderer();
        if (renderer is null) return;
        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to dispose the livery renderer", ex);
        }
    }
}
