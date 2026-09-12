namespace LiveryGallery.Services;

internal sealed class CoalescedAsyncOperation<T>
{
    private readonly object _gate = new();
    private Task<T>? _inFlight;

    public Task<T> RunAsync(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        Task<T> inFlight;
        lock (_gate)
        {
            _inFlight ??= RunCoreAsync(operation, ct);
            inFlight = _inFlight;
        }

        return ct.CanBeCanceled ? WaitWithOwnCancellationAsync(inFlight, ct) : inFlight;
    }

    private async Task<T> RunCoreAsync(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
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
