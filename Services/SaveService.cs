namespace LiveryGallery.Services;

internal class SaveService
{
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private long _generation;
    private string? _pendingJson;
    private string? _pendingPath;

    public void ScheduleSave(string json, string path)
    {
        long myGeneration;
        CancellationToken token;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            myGeneration = ++_generation;
            _pendingJson = json;
            _pendingPath = path;
        }
        _ = SaveDelayedAsync(token, json, path, myGeneration);
    }

    public void SaveImmediate(string json, string path)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _generation++;
            _pendingJson = null;
            _pendingPath = null;
        }
        _writeSemaphore.Wait();
        try { Save(json, path); }
        finally { _writeSemaphore.Release(); }
    }

    public async Task<bool> SaveImmediateAsync(string json, string path)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _generation++;
            _pendingJson = null;
            _pendingPath = null;
        }
        await _writeSemaphore.WaitAsync();
        try { return await SaveAsync(json, path); }
        finally { _writeSemaphore.Release(); }
    }

    public void Flush()
    {
        string? json;
        string? path;
        lock (_lock)
        {
            if (_pendingJson is null || _pendingPath is null) return;
            json = _pendingJson;
            path = _pendingPath;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _generation++;
            _pendingJson = null;
            _pendingPath = null;
        }
        _writeSemaphore.Wait();
        try { Save(json, path); }
        finally { _writeSemaphore.Release(); }
    }

    private async Task SaveDelayedAsync(
        CancellationToken cancellationToken,
        string json,
        string path,
        long myGeneration)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool shouldWrite;
        lock (_lock)
        {
            shouldWrite = myGeneration == _generation;
            if (shouldWrite)
            {
                _pendingJson = null;
                _pendingPath = null;
            }
        }

        if (!shouldWrite) return;

        await _writeSemaphore.WaitAsync();
        try { await SaveAsync(json, path); }
        finally { _writeSemaphore.Release(); }
    }

    private static void Save(string json, string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path)
                ?? throw new Exception();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            AtomicFile.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to save '{path}'", ex);
        }
    }

    private static async Task<bool> SaveAsync(string json, string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path)
                ?? throw new Exception();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            await AtomicFile.WriteAllTextAsync(path, json);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to save '{path}'", ex);
            return false;
        }
    }
}
