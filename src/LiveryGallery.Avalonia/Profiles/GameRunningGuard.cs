using System.Diagnostics;
using ForzaToolkit.GameInput;
using LiveryGallery.Services;

namespace LiveryGallery.Profiles;

/// <summary>
/// Moving livery folders in the save while the game is open is risky: the game may hold the files, not see the change
/// until it restarts, or write its own state over it. Archiving and restoring ask first when the game is running.
/// </summary>
internal sealed class GameRunningGuard(Func<ForzaGame> game)
{
    /// <summary>Test hook: replaces the process check</summary>
    internal Func<bool>? IsRunningOverride { get; set; }

    /// <summary>
    /// True when a process of the game runs. Looks for the process, not the window: a minimized game or one still
    /// loading counts too. False when the check itself fails: the warning is advice, not a lock.
    /// </summary>
    public bool IsRunning()
    {
        if (IsRunningOverride is { } overridden) return overridden();
        if (GameWindowTarget.TryFor(game()) is not { } target) return false;
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(target.ProcessName);
            return processes.Length > 0;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled("GameRunningGuard", "Failed to check whether the game is running", ex);
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    /// <summary>
    /// True when the operation may go on: the game is not running, nobody can be asked (<paramref name="ask"/> is
    /// null), or the user chose to continue
    /// </summary>
    public async Task<bool> ConfirmAsync(Func<Task<bool>>? ask)
    {
        if (ask is null) return true;
        // Off the UI thread: listing processes takes a few milliseconds to tens of them
        bool running = await Task.Run(IsRunning);
        return !running || await ask();
    }
}
