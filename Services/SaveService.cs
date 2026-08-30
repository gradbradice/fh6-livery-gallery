namespace LiveryGallery.Services;

internal class SaveService
{
    private CancellationTokenSource? _cts;
    private readonly Lock _lock = new();

    public void ScheduleSave(string json, string path)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = SaveDelayedAsync(_cts.Token, json, path);
        }
    }

    private static async Task SaveDelayedAsync(
        CancellationToken cancellationToken,
        string json, 
        string path)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            Save(json, path);
        }
        catch(OperationCanceledException)
        {

        }
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
