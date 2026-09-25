using LiveryGallery.Services;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Controller;

internal sealed class RegenerateRequestCoalescer(ScanCoordinator coordinator)
{
    private readonly List<TaskCompletionSource<List<LiveryEntry>>> _pending = [];

    public Task<List<LiveryEntry>> RequestAsync(Func<CancellationToken, Task<List<LiveryEntry>>> regenerate)
    {
        var tcs = new TaskCompletionSource<List<LiveryEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending.Add(tcs);

        _ = coordinator.RunOrReplaceQueuedAsync(async ct =>
        {
            List<TaskCompletionSource<List<LiveryEntry>>> toResolve;
            lock (_pending)
            {
                toResolve = [.. _pending];
                _pending.Clear();
            }

            try
            {
                var entries = await regenerate(ct);
                foreach (var pending in toResolve) pending.TrySetResult(entries);
            }
            catch (Exception ex)
            {
                foreach (var pending in toResolve) pending.TrySetException(ex);
            }
        });

        return tcs.Task;
    }
}
