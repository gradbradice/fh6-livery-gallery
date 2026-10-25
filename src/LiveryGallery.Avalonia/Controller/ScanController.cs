using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Controller;

internal sealed class ScanController
{
    private readonly LiveryScanner scanService;
    private readonly LatestWinsRunner _coordinator = new();
    private readonly RegenerateRequestCoalescer _regenerateCoalescer;

    public ScanController(LiveryScanner scanService)
    {
        this.scanService = scanService;
        _regenerateCoalescer = new RegenerateRequestCoalescer(_coordinator);
    }

    public LiveryScanEntry? LastScanResult { get; private set; }

    public void RequestOrphanCleanup() => scanService.RequestOrphanCleanup();

    public Task WaitAsync() => _coordinator.WaitAsync();

    public void Cancel() => _coordinator.Cancel();

    public bool TryStartScan(
        string accountKey,
        string savePath,
        ulong? currentUserId,
        bool needEntries,
        bool collectChanges,
        IProgress<string>? progress,
        CancellationToken ct,
        out Task<LiveryScanEntry> resultTask)
    {
        var tcs = new TaskCompletionSource<LiveryScanEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        resultTask = tcs.Task;

        // The scan stops on Cancel() (coordinator) as well as on the caller's token.
        return _coordinator.TryRun(async coordinatorToken =>
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(coordinatorToken, ct);
                var result = await scanService.ScanAsync(accountKey, savePath, currentUserId, needEntries, collectChanges, progress, linked.Token);
                LastScanResult = result with { Entries = [], ChangesSinceLastLaunch = null };
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
    }

    public Task<List<LiveryEntry>> RegenerateEntriesAsync(string accountKey, ulong? currentUserId) =>
        _regenerateCoalescer.RequestAsync(ct => scanService.RegenerateEntriesAsync(accountKey, currentUserId, ct));
}
