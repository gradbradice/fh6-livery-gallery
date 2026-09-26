namespace LiveryGallery.Services;

internal sealed class CoalescedAsyncOperation<T>
{
    private readonly object _gate = new();
    private Task<T>? _inFlight;

    public Task<T> RunAsync(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        Task<T> inFlight;
        bool startedNew;
        lock (_gate)
        {
            startedNew = _inFlight is null;
            if (startedNew)
            {
                inFlight = RunCoreAsync(operation, ct);
                if (!inFlight.IsCompleted) _inFlight = inFlight;
            }
            else
            {
                inFlight = _inFlight!;
            }
        }

        if (startedNew || !ct.CanBeCanceled) return inFlight;
        return WaitWithOwnCancellationAsync(inFlight, ct);
    }

    private async Task<T> RunCoreAsync(Func<CancellationToken, Task<T>> operation, CancellationToken starterToken)
    {
        using var sharedCts = CancellationTokenSource.CreateLinkedTokenSource(starterToken);
        try
        {
            return await operation(sharedCts.Token);
        }
        finally
        {
            lock (_gate) _inFlight = null;
        }
    }

    private static async Task<T> WaitWithOwnCancellationAsync(Task<T> inFlight, CancellationToken ct)
    {
        var cancellationTask = Task.Delay(Timeout.Infinite, ct);
        var completed = await Task.WhenAny(inFlight, cancellationTask);
        if (completed == cancellationTask)
            ct.ThrowIfCancellationRequested();
        return await inFlight;
    }
}
