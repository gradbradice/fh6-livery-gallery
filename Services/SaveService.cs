namespace LiveryGallery.Services;

internal class SaveService
{
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();
    private long _generation;

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
        }
        Save(json, path);
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
        }

        Save(json, path);
    }

    private static void Save(string json, string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path)
                ?? throw new Exception();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {

        }
    }
}
