namespace LiveryGallery.Services;

internal sealed class ScanCoordinator
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _currentTask;
    private Func<CancellationToken, Task>? _queuedOperation;
    private bool _workerActive;

    public bool IsRunning
    {
        get { lock (_lock) return _workerActive; }
    }
    
    public Task Current => _currentTask ?? Task.CompletedTask;

    public bool TryRun(Func<CancellationToken, Task> operation)
    {
        lock (_lock)
        {
            if (_workerActive) return false;
            _workerActive = true;
            _currentTask = RunWorkerAsync(operation);
        }
        return true;
    }

    public Task RunOrReplaceQueuedAsync(Func<CancellationToken, Task> operation)
    {
        lock (_lock)
        {
            if (!_workerActive)
            {
                _workerActive = true;
                _currentTask = RunWorkerAsync(operation);
                return _currentTask;
            }

            _queuedOperation = operation;
            return _currentTask!;
        }
    }

    private async Task RunWorkerAsync(Func<CancellationToken, Task> firstOperation)
    {
        var operation = firstOperation;
        while (true)
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
            catch (Exception ex)
            {
                AppLogger.LogError("Unhandled exception in a scan/regenerate operation", ex);
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
                if (next is null)
                {
                    _workerActive = false;
                    return;
                }
            }
            operation = next;
        }
    }

    public void Cancel()
    {
        lock (_lock) _cts?.Cancel();
    }

    public Task WaitAsync() => Current;
}
