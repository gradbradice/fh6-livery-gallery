using Avalonia.Threading;
using ForzaToolkit.Crypto.Client;
using LiveryGallery.Enums;
using LiveryGallery.Services;
using LiveryGallery.ViewModels;
using System.Net.NetworkInformation;

namespace LiveryGallery.Controller;

internal sealed class ServerConnectionController : IDisposable
{
    private static readonly TimeSpan MinTimerInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private sealed class Session
    {
        private readonly CancellationTokenSource _cts = new();
        public CancellationToken Token => _cts.Token;
        public bool IsCancelled => _cts.IsCancellationRequested;
        public bool CheckRunning;
        public bool SyncRunning;
        public void Cancel() => _cts.Cancel();
    }

    private readonly LiveryServerService _service;
    private readonly ServerStatusViewModel _status;
    private readonly Func<string?> _resolveSaveDataPath;
    private readonly Action<IReadOnlyDictionary<string, int>?> _applyInstalled;
    private readonly ServerUploadState _uploads;
    private readonly DispatcherTimer _checkTimer = new();
    private readonly DispatcherTimer _syncTimer = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly HashSet<Task> _inFlight = [];

    private Session? _session;
    private bool _enabled;
    private bool _paused;
    private bool _disposed;
    private int _unreachableCount;
    private ServerConnectionStatus? _lastStatus;// null = unknown, or the connection just failed
    private bool _waitsForUser; // update / certificate / rejected build: no automatic checks
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private DateTime? _nextCheckDueUtc;

    public ServerConnectionController(
        LiveryServerService service,
        InstalledLiveriesCache cache,
        ServerStatusViewModel status,
        Func<string?> resolveSaveDataPath,
        Action<IReadOnlyDictionary<string, int>?> applyInstalled)
    {
        _service = service;
        _status = status;
        _resolveSaveDataPath = resolveSaveDataPath;
        _applyInstalled = applyInstalled;
        _uploads = new ServerUploadState(cache);
        _checkTimer.Tick += (_, __) =>
        {
            _checkTimer.Stop();
            _nextCheckDueUtc = null;
            Launch(CheckAsync);
        };
        _syncTimer.Tick += (_, __) =>
        {
            _syncTimer.Stop();
            Launch(SyncAsync);
        };
    }

    public bool IsEnabled => _enabled;

    public void Configure(bool enabled)
    {
        if (_disposed || enabled == _enabled) return;
        _enabled = enabled;
        if (enabled) Start();
        else Stop();
    }

    public void SetWindowVisible(bool visible)
    {
        if (_paused == !visible) return;
        _paused = !visible;
        if (!_enabled || _disposed) return;

        if (_paused)
        {
            _checkTimer.Stop();
            _syncTimer.Stop();
            return;
        }

        if (_waitsForUser) return;
        if (_lastStatus is null || _nextCheckDueUtc is not { } due || due <= DateTime.UtcNow)
            Launch(CheckAsync);
        else
            Schedule(_checkTimer, due - DateTime.UtcNow);
        if (_uploads.SyncWanted) ScheduleSync(ServerSyncPolicy.SyncDebounce);
    }

    public void OnWindowActivated()
    {
        if (!_enabled || _paused || _waitsForUser) return;
        if (DateTime.UtcNow - _lastCheckUtc >= ServerSyncPolicy.FocusRecheckAfter) Launch(CheckAsync);
    }

    public void RecheckNow()
    {
        if (!_enabled || _paused) return;
        if (DateTime.UtcNow - _lastCheckUtc < ServerSyncPolicy.ManualRecheckMinInterval) return;
        _waitsForUser = false;
        Launch(CheckAsync);
    }

    public void RequestSync()
    {
        if (!_enabled || _disposed) return;
        _uploads.SyncWanted = true;
        ScheduleSync(ServerSyncPolicy.SyncDebounce);
    }

    public async Task<bool> IsInstalledResultOutdatedAsync(CancellationToken ct)
    {
        string? shownHash = _uploads.UploadedHash;
        string? shownPath = _uploads.ResultProfilePath;
        if (!_enabled || _disposed || shownHash is null || shownPath is null) return true;

        string? saveDataPath = _resolveSaveDataPath();
        bool outdated;
        try
        {
            outdated = await Task.Run(async () =>
            {
                string? path = LiveryServerService.FindProfileData(saveDataPath);
                if (path is null || !string.Equals(path, shownPath, StringComparison.OrdinalIgnoreCase)) return true;
                var (_, hash) = await LiveryServerService.ReadProfileDataAsync(path, ct).ConfigureAwait(false);
                return hash != shownHash;
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled("LiveryServer.OutdatedCheck", "Failed to compare the save with the last synced one", ex);
            outdated = true;
        }

        if (outdated) RequestSync();
        return outdated;
    }

    public void OnSavePathChanged()
    {
        if (!_enabled || _disposed) return;
        _uploads.ForgetResult();
        _applyInstalled(null);
        _status.SetSync(InstalledSyncState.None);
        RequestSync();
    }

    private void Start()
    {
        if (!_service.IsAvailable)
        {
            _status.SetConnection(ServerIndicatorState.NotConfigured);
            return;
        }
        _session = new Session();
        _uploads.SyncWanted = true;
        _status.SetConnection(ServerIndicatorState.Checking);
        RestoreSnapshot();
        if (!_paused) Launch(CheckAsync);
    }

    private void RestoreSnapshot()
    {
        string? profilePath = LiveryServerService.FindProfileData(_resolveSaveDataPath());
        if (_uploads.Restore(profilePath, DateTime.UtcNow) is not { Installed: { } installed, ResultAtUtc: { } resultAt }) return;

        // The sync still reads and hashes the save
        // if it is unchanged, nothing is sent
        _applyInstalled(installed);
        _status.SetSync(InstalledSyncState.Synced, installed.Count, resultAt.ToLocalTime());
    }

    private void Stop()
    {
        CancelSession();
        _lastStatus = null;
        _waitsForUser = false;
        _unreachableCount = 0;
        _nextCheckDueUtc = null;
        // Switched off: forget everything learned from the server
        _uploads.Reset();
        _applyInstalled(null);
        _status.SetHidden();
    }

    private void CancelSession()
    {
        _session?.Cancel();
        _session = null;
        _checkTimer.Stop();
        _syncTimer.Stop();
    }

    private bool IsCurrent(Session session) =>
        _enabled && !_disposed && ReferenceEquals(session, _session) && !session.IsCancelled;

    private void Launch(Func<Task> operation)
    {
        if (_disposed) return;
        var task = operation();
        if (task.IsCompleted) return;
        _inFlight.Add(task);
        _ = ForgetWhenDoneAsync(task);
    }

    private async Task ForgetWhenDoneAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        _inFlight.Remove(task);
    }

    private async Task CheckAsync()
    {
        var session = _session;
        if (session is null || !_enabled || _paused || session.CheckRunning) return;
        session.CheckRunning = true;
        _checkTimer.Stop();
        var ct = session.Token;
        try
        {
            if (_lastStatus is null) _status.SetConnection(ServerIndicatorState.Checking);

            ServerConnectionStatus? st;
            await _requestGate.WaitAsync(ct);
            try { st = await _service.CheckConnectionAsync(ct); }
            finally { _requestGate.Release(); }
            if (!IsCurrent(session)) return;

            _lastCheckUtc = DateTime.UtcNow;
            if (st is null)
            {
                _status.SetConnection(ServerIndicatorState.NotConfigured);
                return;
            }

            _lastStatus = st;
            _unreachableCount = st.State == ConnectionState.Unreachable ? _unreachableCount + 1 : 0;
            _status.SetConnection(
                ServerSyncPolicy.ToIndicator(st.State, st.State != ConnectionState.Unreachable || IsNetworkAvailable()),
                st.Server?.Version, st.Server?.ApiVersion, st.Latency, st.ServerMessage, st.RetryAfter);

            var delay = ServerSyncPolicy.NextCheckDelay(st, _unreachableCount);
            _waitsForUser = delay is null;
            if (delay is { } d) ScheduleCheck(d);

            if (st.CanUpload && _uploads.SyncWanted) ScheduleSync(TimeSpan.Zero);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Switched off or shutting down
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Livery server status check failed", ex);
        }
        finally
        {
            session.CheckRunning = false;
        }
    }

    private async Task SyncAsync()
    {
        var session = _session;
        if (session is null || !_enabled || _paused || session.SyncRunning) return;
        if (_lastStatus is not { CanUpload: true }) return; // resumes after a good status check

        var now = DateTime.UtcNow;
        if (now < _uploads.BlockedUntilUtc)
        {
            Schedule(_syncTimer, _uploads.BlockedUntilUtc - now);
            return;
        }

        session.SyncRunning = true;
        var ct = session.Token;
        string? hash = null;
        try
        {
            string? path = LiveryServerService.FindProfileData(_resolveSaveDataPath());
            if (path is null)
            {
                _uploads.SyncWanted = false;
                _status.SetSync(InstalledSyncState.ProfileNotFound);
                return;
            }

            var (data, dataHash) = await Task.Run(() => LiveryServerService.ReadProfileDataAsync(path, ct), ct);
            hash = dataHash;
            if (!IsCurrent(session)) return;
            if (_uploads.IsAlreadyHandled(hash))
            {
                _uploads.SyncWanted = false; // nothing new to send
                return;
            }

            var wait = _uploads.TimeUntilUploadAllowed(DateTime.UtcNow);
            if (wait > TimeSpan.Zero)
            {
                Schedule(_syncTimer, wait); // send it when allowed
                return;
            }

            _uploads.RecordUploadStarted(DateTime.UtcNow);
            _status.SetSync(InstalledSyncState.Syncing);
            IReadOnlyDictionary<string, int> installed;
            await _requestGate.WaitAsync(ct);
            try { installed = await _service.GetInstalledLiveriesAsync(data, ct); }
            finally { _requestGate.Release(); }
            if (!IsCurrent(session)) return;

            _uploads.RecordResult(path, hash, installed, DateTime.UtcNow);
            _applyInstalled(installed);
            _status.SetSync(InstalledSyncState.Synced, installed.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Switched off or shutting down
        }
        catch (LiveryServiceException ex)
        {
            if (IsCurrent(session)) HandleUploadError(ex, hash);
        }
        catch (Exception ex)
        {
            // Couldn't read the save, try again a bit later.
            AppLogger.LogErrorThrottled("LiveryServer.Sync", "Failed to update installed liveries", ex);
            if (IsCurrent(session))
            {
                _status.SetSync(InstalledSyncState.Failed);
                _uploads.BlockUploads(DateTime.UtcNow + TimeSpan.FromMinutes(1));
                ScheduleSync(TimeSpan.FromMinutes(1));
            }
        }
        finally
        {
            session.SyncRunning = false;
        }
    }

    private void HandleUploadError(LiveryServiceException ex, string? hash)
    {
        AppLogger.LogErrorThrottled($"LiveryServer.Upload.{ex.Error}", $"The livery server did not process the save ({ex.Error})", ex);

        var (sync, retryAfter, rejectFile) = ServerSyncPolicy.ForUploadError(ex);
        if (rejectFile) _uploads.RecordRejected(hash);
        if (retryAfter is { } retry) _uploads.BlockUploads(DateTime.UtcNow + retry);

        if (ServerSyncPolicy.ConnectionStateFor(ex) is { } state)
        {
            var status = new ServerConnectionStatus(state, null, null, ex, null, DateTimeOffset.UtcNow);
            _lastStatus = null;
            _unreachableCount = state == ConnectionState.Unreachable ? _unreachableCount + 1 : 0;
            _status.SetConnection(ServerSyncPolicy.ToIndicator(state, state != ConnectionState.Unreachable || IsNetworkAvailable()),
                serverMessage: status.ServerMessage, retryAfter: status.RetryAfter);
            _status.SetSync(sync);
            var delay = ServerSyncPolicy.NextCheckDelay(status, _unreachableCount);
            _waitsForUser = delay is null;
            if (delay is { } d) ScheduleCheck(d);
            return;
        }

        _status.SetSync(sync);
        if (!rejectFile && retryAfter is { } wait) ScheduleSync(wait);
    }

    private void ScheduleCheck(TimeSpan delay)
    {
        _nextCheckDueUtc = DateTime.UtcNow + delay;
        if (!_paused && !_disposed) Schedule(_checkTimer, delay);
    }

    private void ScheduleSync(TimeSpan delay)
    {
        if (!_enabled || _paused || _disposed) return;
        var blocked = _uploads.BlockedUntilUtc - DateTime.UtcNow;
        Schedule(_syncTimer, blocked > delay ? blocked : delay);
    }

    private static void Schedule(DispatcherTimer timer, TimeSpan delay)
    {
        timer.Stop();
        timer.Interval = delay < MinTimerInterval ? MinTimerInterval : delay;
        timer.Start();
    }

    private static bool IsNetworkAvailable()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch (Exception ex)
        {
            // Only decides between "no internet" and "server unreachable" in the indicator
            AppLogger.LogErrorThrottled("ServerConnection.IsNetworkAvailable", "Failed to query network availability", ex);
            return true;
        }
    }

    public async Task StopAsync()
    {
        Dispose();
        if (_inFlight.Count == 0) return;
        var pending = _inFlight.ToArray();
        try
        {
            await Task.WhenAll(pending).WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            AppLogger.LogWarning($"{pending.Count(t => !t.IsCompleted)} livery server request(s) still running {StopTimeout.TotalSeconds:0} s after shutdown began");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("A livery server request failed during shutdown", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelSession();
    }
}
