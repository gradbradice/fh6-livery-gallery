using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using System.Globalization;
using System.Text;

namespace LiveryGallery.ViewModels;

internal sealed partial class ServerStatusViewModel : ObservableObject
{
    private ServerIndicatorState _state = ServerIndicatorState.Hidden;
    private InstalledSyncState _sync = InstalledSyncState.None;
    private string? _serverVersion;
    private int? _apiVersion;
    private TimeSpan? _latency;
    private string? _serverMessage;
    private TimeSpan? _retryAfter;
    private int _installedCount;
    private DateTime? _syncedAt;

    public ServerIndicatorState State => _state;
    public bool IsVisible => _state != ServerIndicatorState.Hidden;
    public bool IsGreen => _state == ServerIndicatorState.Online && !HasSyncProblem;
    public bool IsYellow => _state is ServerIndicatorState.Degraded or ServerIndicatorState.RateLimited or ServerIndicatorState.UpdateRequired
        || (_state == ServerIndicatorState.Online && HasSyncProblem);
    public bool IsBlue => _state == ServerIndicatorState.Maintenance;
    public bool IsRed => _state is ServerIndicatorState.AppRejected or ServerIndicatorState.CertificateError or ServerIndicatorState.ServerError;
    public bool IsGray => !IsGreen && !IsYellow && !IsBlue && !IsRed;

    private bool HasSyncProblem => _sync is InstalledSyncState.SaveNotSupported or InstalledSyncState.SaveRejected;

    public string Text => _state switch
    {
        ServerIndicatorState.Checking => Strings.ServerStatusChecking,
        ServerIndicatorState.Online when _sync == InstalledSyncState.SaveNotSupported => Strings.ServerStatusSaveNotSupported,
        ServerIndicatorState.Online when _sync == InstalledSyncState.Syncing => Strings.ServerStatusSyncing,
        ServerIndicatorState.Online => Strings.ServerStatusOnline,
        ServerIndicatorState.Degraded => Strings.ServerStatusDegraded,
        ServerIndicatorState.Maintenance => Strings.ServerStatusMaintenance,
        ServerIndicatorState.Unreachable => Strings.ServerStatusUnreachable,
        ServerIndicatorState.NoInternet => Strings.ServerStatusNoInternet,
        ServerIndicatorState.RateLimited => Strings.ServerStatusRateLimited,
        ServerIndicatorState.UpdateRequired => Strings.ServerStatusUpdateRequired,
        ServerIndicatorState.AppRejected => Strings.ServerStatusAppRejected,
        ServerIndicatorState.CertificateError => Strings.ServerStatusCertificateError,
        ServerIndicatorState.ServerError => Strings.ServerStatusServerError,
        ServerIndicatorState.NotConfigured => Strings.ServerStatusNotConfigured,
        _ => string.Empty
    };

    public string Tooltip
    {
        get
        {
            var text = new StringBuilder();
            void Line(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (text.Length > 0) text.AppendLine();
                text.Append(value);
            }

            if (_serverVersion is not null)
            {
                string details = string.Format(Strings.ServerStatusVersionFormat, _serverVersion);
                if (_apiVersion is int api) details += $" · API v{api}";
                if (_latency is { } latency) details += $" · {latency.TotalMilliseconds:0} ms";
                Line(details);
            }
            Line(_serverMessage);
            Line(_state switch
            {
                ServerIndicatorState.Unreachable or ServerIndicatorState.NoInternet or ServerIndicatorState.ServerError => Strings.ServerStatusRetryHint,
                ServerIndicatorState.RateLimited when _retryAfter is { } wait =>
                    string.Format(Strings.ServerStatusRetryInFormat, Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))),
                ServerIndicatorState.UpdateRequired => Strings.ServerStatusUpdateHint,
                ServerIndicatorState.AppRejected => Strings.ServerStatusAppRejectedHint,
                ServerIndicatorState.CertificateError => Strings.ServerStatusCertificateHint,
                _ => null
            });
            Line(_sync switch
            {
                InstalledSyncState.Synced when _syncedAt is { } at => string.Format(Strings.ServerSyncDoneFormat,
                    _installedCount, at.ToString("t", CultureInfo.CurrentCulture)),
                InstalledSyncState.Syncing => Strings.ServerStatusSyncing,
                InstalledSyncState.ProfileNotFound => Strings.ServerSyncProfileNotFound,
                InstalledSyncState.SaveNotSupported => Strings.ServerSyncSaveNotSupported,
                InstalledSyncState.SaveRejected => Strings.ServerSyncSaveRejected,
                InstalledSyncState.Failed => Strings.ServerSyncFailed,
                _ => null
            });
            if (_state is not (ServerIndicatorState.Hidden or ServerIndicatorState.Checking or ServerIndicatorState.NotConfigured))
                Line(Strings.ServerStatusClickToRecheck);
            return text.ToString();
        }
    }

    public void SetHidden()
    {
        _state = ServerIndicatorState.Hidden;
        _sync = InstalledSyncState.None;
        _serverVersion = null;
        _apiVersion = null;
        _latency = null;
        _serverMessage = null;
        _retryAfter = null;
        _syncedAt = null;
        _installedCount = 0;
        RaiseAll();
    }

    public void SetConnection(ServerIndicatorState state, string? serverVersion = null, int? apiVersion = null,
        TimeSpan? latency = null, string? serverMessage = null, TimeSpan? retryAfter = null)
    {
        _state = state;
        if (state != ServerIndicatorState.Checking)
        {
            _serverVersion = serverVersion;
            _apiVersion = apiVersion;
            _latency = latency;
            _serverMessage = serverMessage;
            _retryAfter = retryAfter;
        }
        RaiseAll();
    }

    public void SetSync(InstalledSyncState sync, int installedCount = 0, DateTime? syncedAt = null)
    {
        _sync = sync;
        if (sync == InstalledSyncState.Synced)
        {
            _installedCount = installedCount;
            _syncedAt = syncedAt ?? DateTime.Now;
        }
        RaiseAll();
    }

    public void RefreshLocalizedText() => RaiseAll();

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsGreen));
        OnPropertyChanged(nameof(IsYellow));
        OnPropertyChanged(nameof(IsBlue));
        OnPropertyChanged(nameof(IsRed));
        OnPropertyChanged(nameof(IsGray));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Tooltip));
    }
}
