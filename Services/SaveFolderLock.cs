namespace LiveryGallery.Services;

internal sealed class SaveFolderLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _closing = new();

    public CancellationToken ClosingToken => _closing.Token;

    public bool IsClosing => _closing.IsCancellationRequested;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closing.Token);
        await _semaphore.WaitAsync(linked.Token);
        return new Releaser(_semaphore);
    }

    public async Task CloseAsync()
    {
        _closing.Cancel();
        await _semaphore.WaitAsync();
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) semaphore.Release();
        }
    }
}
