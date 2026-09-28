namespace LiveryGallery.Enums;

internal enum ServerIndicatorState
{
    Hidden,
    Checking,
    Online,
    Degraded,
    Maintenance,
    Unreachable,
    NoInternet,
    RateLimited,
    UpdateRequired,
    AppRejected,
    CertificateError,
    ServerError,
    NotConfigured,
}

internal enum InstalledSyncState
{
    None,
    Syncing,
    Synced,
    ProfileNotFound,
    SaveNotSupported,
    SaveRejected,
    Failed,
}
