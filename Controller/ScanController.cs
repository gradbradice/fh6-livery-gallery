using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.Controller;

internal sealed class ScanController(LiveryScanService scanService)
{
    private readonly ScanCoordinator _coordinator = new();
    public LiveryScanEntry? LastScanResult { get; private set; }

    public Task WaitAsync() => _coordinator.WaitAsync();

    public void Cancel() => _coordinator.Cancel();

    public bool TryRunScan(
        string savePath,
        IProgress<string>? progress,
        Action<List<LiveryEntry>> onEntriesReady,
        Action<Exception> onError)
    {
        return _coordinator.TryRun(async ct =>
        {
            try
            {
                var result = await scanService.ScanAsync(savePath, progress, ct);
                LastScanResult = result;
                onEntriesReady(result.Entries);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        });
    }

    public Task RegenerateEntriesAsync(Action<List<LiveryEntry>> onEntriesReady) =>
        _coordinator.RunOrQueueAsync(async ct =>
        {
            var entries = await scanService.RegenerateEntriesAsync(ct);
            onEntriesReady(entries);
        });
}
