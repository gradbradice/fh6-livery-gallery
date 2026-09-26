namespace LiveryGallery.Services;

internal static class PersistenceManager
{
    private sealed class PathState
    {
        public CancellationTokenSource? Cts;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
        public long Generation;
        public string? PendingJson;
        public Task<bool>? InFlightWrite;
    }

    private static readonly Dictionary<string, PathState> _paths = [];
    private static readonly Lock _lock = new();

    private static PathState GetState(string path)
    {
        lock (_lock)
        {
            if (!_paths.TryGetValue(path, out var state))
                _paths[path] = state = new PathState();
            return state;
        }
    }

    public static void Schedule(string path, string json)
    {
        var state = GetState(path);
        lock (_lock)
        {
            state.Cts?.Cancel();
            state.Cts?.Dispose();
            state.Cts = new CancellationTokenSource();
            long myGeneration = ++state.Generation;
            state.PendingJson = json;
            state.InFlightWrite = DelayedWriteAsync(state, path, json, state.Cts.Token, myGeneration);
        }
    }

    public static async Task<bool> SaveNowAsync(string path, string json)
    {
        var state = GetState(path);
        Task<bool> task;
        lock (_lock)
        {
            state.Cts?.Cancel();
            state.Cts?.Dispose();
            state.Cts = null;
            long myGeneration = ++state.Generation;
            state.PendingJson = null;
            task = WriteWithSemaphoreAsync(state, path, json, myGeneration);
            state.InFlightWrite = task;
        }
        return await task;
    }

    public static async Task<bool> FlushAsync()
    {
        List<KeyValuePair<string, PathState>> snapshot;
        lock (_lock) snapshot = [.. _paths];

        bool allOk = true;
        foreach (var (path, state) in snapshot)
        {
            string? pendingJson;
            long myGeneration;
            Task<bool>? previousInFlight;
            lock (_lock)
            {
                previousInFlight = state.InFlightWrite;
                pendingJson = state.PendingJson;
                if (pendingJson is not null)
                {
                    state.Cts?.Cancel();
                    state.Cts?.Dispose();
                    state.Cts = null;
                    myGeneration = ++state.Generation;
                    state.PendingJson = null;
                }
                else
                {
                    myGeneration = state.Generation;
                }
            }

            if (pendingJson is not null)
            {
                allOk &= await WriteWithSemaphoreAsync(state, path, pendingJson, myGeneration);
            }
            else if (previousInFlight is not null)
            {
                try { allOk &= await previousInFlight; }
                catch { allOk = false; }
            }
        }
        return allOk;
    }

    private static async Task<bool> DelayedWriteAsync(
        PathState state, string path, string json, CancellationToken ct, long myGeneration)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        bool shouldWrite;
        lock (_lock)
        {
            shouldWrite = myGeneration == state.Generation;
            if (shouldWrite) state.PendingJson = null;
        }
        if (!shouldWrite) return false;

        return await WriteWithSemaphoreAsync(state, path, json, myGeneration);
    }

    private static async Task<bool> WriteWithSemaphoreAsync(PathState state, string path, string json, long myGeneration)
    {
        Task<bool>? supersededBy = null;
        bool result = false;
        await state.WriteLock.WaitAsync();
        try
        {
            bool isCurrent;
            lock (_lock) isCurrent = myGeneration == state.Generation;
            if (isCurrent)
                result = await WriteFileAsync(path, json);
            else
                lock (_lock) supersededBy = state.InFlightWrite;
        }
        finally { state.WriteLock.Release(); }
        if (supersededBy is null) return result;
        return await supersededBy;
    }

    private static async Task<bool> WriteFileAsync(string path, string json)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path) ?? throw new Exception();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await AtomicFile.WriteAllTextAsync(path, json);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to save '{path}'", ex);
            return false;
        }
    }
}
