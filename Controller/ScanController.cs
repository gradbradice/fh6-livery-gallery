using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Controller;

internal sealed class ScanController
{
    private readonly LiveryScanner scanService;
    private readonly ScanCoordinator _coordinator = new();
    private readonly RegenerateRequestCoalescer _regenerateCoalescer;

    public ScanController(LiveryScanner scanService)
    {
        this.scanService = scanService;
        _regenerateCoalescer = new RegenerateRequestCoalescer(_coordinator);
    }

    public LiveryScanEntry? LastScanResult { get; private set; }

    public Task WaitAsync() => _coordinator.WaitAsync();

    public void Cancel() => _coordinator.Cancel();

    public bool TryStartScan(
        string savePath,
        ulong? currentUserId,
        bool needEntries,
        IProgress<string>? progress,
        out Task<LiveryScanEntry> resultTask)
    {
        var tcs = new TaskCompletionSource<LiveryScanEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        resultTask = tcs.Task;

        return _coordinator.TryRun(async ct =>
        {
            try
            {
                var result = await scanService.ScanAsync(savePath, currentUserId, needEntries, progress, ct);
                LastScanResult = result with { Entries = [] };
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
    }

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(ulong? currentUserId) =>
        _regenerateCoalescer.RequestAsync(ct => scanService.RegenerateEntriesAsync(currentUserId, ct));
}
