namespace LiveryGallery.Services;

internal sealed class ScanCoordinator
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _currentTask;
    private Func<CancellationToken, Task>? _queuedOperation;

    public bool IsRunning => _currentTask is { IsCompleted: false };
    
    public Task Current => _currentTask ?? Task.CompletedTask;

    public bool TryRun(Func<CancellationToken, Task> operation)
    {
        lock (_lock)
        {
            if (IsRunning) return false;
            _currentTask = StartAsync(operation);
        }
        return true;
    }

    public Task RunOrReplaceQueuedAsync(Func<CancellationToken, Task> operation)
    {
        lock (_lock)
        {
            if (!IsRunning)
            {
                _currentTask = StartAsync(operation);
                return _currentTask;
            }

            _queuedOperation = operation;
            return _currentTask!;
        }
    }

    private async Task StartAsync(Func<CancellationToken, Task> operation)
    {
        var cts = new CancellationTokenSource();
        lock (_lock) _cts = cts;
        try
        {
            await operation(cts.Token);
        }
        catch (OperationCanceledException)
        {
            
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_cts, cts)) _cts = null;
            }
            cts.Dispose();
        }

        Func<CancellationToken, Task>? next;
        lock (_lock)
        {
            next = _queuedOperation;
            _queuedOperation = null;
        }

        if (next is not null)
        {
            Task nextTask;
            lock (_lock) nextTask = _currentTask = StartAsync(next);
            await nextTask;
        }
    }

    public void Cancel()
    {
        lock (_lock) _cts?.Cancel();
    }

    public Task WaitAsync() => Current;
}
