using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal sealed class DialogWorkScope
{
    private readonly Window _window;
    private readonly IAsyncRelayCommand[] _commands;
    private readonly BackgroundTaskTracker _tasks = new();
    private bool _closeQueued;

    public DialogWorkScope(Window window, params IAsyncRelayCommand[] commands)
    {
        _window = window;
        _commands = commands;
        window.Closing += OnClosing;
    }

    public void Run(Func<Task> work, string context) => _ = _tasks.Run(work, context);

    public bool IsBusy => PendingWork() is not null;

    private Task? PendingWork()
    {
        var pending = new List<Task>();
        foreach (var command in _commands)
            if (command.ExecutionTask is { IsCompleted: false } running) pending.Add(running);
        if (_tasks.RunningCount > 0) pending.Add(_tasks.WaitAllAsync(Timeout.InfiniteTimeSpan));
        return pending.Count == 0 ? null : Task.WhenAll(pending);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (PendingWork() is not { } pending) return;
        e.Cancel = true;
        if (_closeQueued) return;
        _closeQueued = true;
        _ = CloseWhenDoneAsync(pending);
    }

    private async Task CloseWhenDoneAsync(Task pending)
    {
        await pending.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        _closeQueued = false;
        _window.Close();
    }
}
