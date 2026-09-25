namespace LiveryGallery.Services;

internal sealed class SaveFolderLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Releaser(_semaphore);
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
