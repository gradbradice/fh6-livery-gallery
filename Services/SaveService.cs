namespace LiveryGallery.Services;

internal class SaveService
{
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();
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
            Save(json, path);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_pendingJson is null || _pendingPath is null) return;
            string json = _pendingJson;
            string path = _pendingPath;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _generation++;
            _pendingJson = null;
            _pendingPath = null;
            Save(json, path);
        }
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

        lock (_lock)
        {
            if (myGeneration != _generation) return;
            _pendingJson = null;
            _pendingPath = null;
            Save(json, path);
        }
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
}
