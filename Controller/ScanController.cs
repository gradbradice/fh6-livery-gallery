using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Controller;

internal sealed class ScanController(LiveryScanner scanService)
{
    private readonly ScanCoordinator _coordinator = new();
    public LiveryScanEntry? LastScanResult { get; private set; }

    public Task WaitAsync() => _coordinator.WaitAsync();

    public void Cancel() => _coordinator.Cancel();

    public bool TryStartScan(
        string savePath,
        ulong? currentUserId,
        IProgress<string>? progress,
        out Task<LiveryScanEntry> resultTask)
    {
        var tcs = new TaskCompletionSource<LiveryScanEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        resultTask = tcs.Task;

        return _coordinator.TryRun(async ct =>
        {
            try
            {
                var result = await scanService.ScanAsync(savePath, currentUserId, progress, ct);
                LastScanResult = result;
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
    }

    public Task RegenerateEntriesAsync(ulong? currentUserId, Action<List<LiveryEntry>> onEntriesReady) =>
        _coordinator.RunOrReplaceQueuedAsync(async ct =>
        {
            var entries = await scanService.RegenerateEntriesAsync(currentUserId, ct);
            onEntriesReady(entries);
        });
}
