namespace LiveryGallery.Services;

internal sealed class BackgroundTaskTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Task, string> _running = [];

    public int RunningCount
    {
        get { lock (_lock) return _running.Count; }
    }

    public Task Run(Func<Task> work, string context)
    {
        Task task;
        try
        {
            task = work();
        }
        catch (Exception ex)
        {
            task = Task.FromException(ex);
        }
        return Track(task, context);
    }

    public Task Track(Task task, string context)
    {
        if (task.IsCompleted)
        {
            LogIfFailed(task, context);
            return Task.CompletedTask;
        }

        lock (_lock) _running[task] = context;
        return ObserveAsync(task, context);
    }

    private async Task ObserveAsync(Task task, string context)
    {
        try
        {
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            LogIfFailed(task, context);
        }
        finally
        {
            lock (_lock) _running.Remove(task);
        }
    }

    private static void LogIfFailed(Task task, string context)
    {
        if (task.IsFaulted && task.Exception is { } aggregate && aggregate.InnerExceptions.Any(e => e is not OperationCanceledException))
            AppLogger.LogError($"Background operation failed: {context}", aggregate.Flatten());
    }

    public async Task<bool> WaitAllAsync(TimeSpan timeout)
    {
        bool unlimited = timeout == Timeout.InfiniteTimeSpan;
        var deadline = unlimited ? DateTime.MaxValue : DateTime.UtcNow + timeout;
        while (true)
        {
            Task[] pending;
            lock (_lock) pending = [.. _running.Keys];
            if (pending.Length == 0) return true;

            var remaining = unlimited ? Timeout.InfiniteTimeSpan : deadline - DateTime.UtcNow;
            if (!unlimited && remaining <= TimeSpan.Zero)
            {
                string names;
                lock (_lock) names = string.Join(", ", _running.Values);
                AppLogger.LogWarning($"Shutdown stopped waiting for background work after {timeout.TotalSeconds:0.#} s: {names}");
                return false;
            }

            await Task.WhenAll(pending).WaitAsync(remaining).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            lock (_lock)
                foreach (var task in pending)
                    if (task.IsCompleted) _running.Remove(task);
        }
    }
}
