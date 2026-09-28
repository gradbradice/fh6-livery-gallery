using LiveryGallery.Services;

namespace LiveryGallery.Controller;

internal sealed class ServerUploadState(InstalledLiveriesCache cache)
{
    private InstalledLiveriesCache.Snapshot? _snapshot;

    public DateTime LastUploadUtc { get; private set; } = DateTime.MinValue;
    public DateTime BlockedUntilUtc { get; private set; } = DateTime.MinValue;
    public string? UploadedHash { get; private set; }
    public string? RejectedHash { get; private set; }
    public bool SyncWanted { get; set; }
    public string? ResultProfilePath => _snapshot?.ProfilePath;

    public InstalledLiveriesCache.Snapshot? Restore(string? profilePath, DateTime nowUtc)
    {
        _snapshot = cache.Load();
        if (_snapshot is null) return null;

        if (_snapshot.LastUploadUtc is { } lastUpload)
        {
            var restored = lastUpload > nowUtc ? nowUtc : lastUpload;
            if (restored > LastUploadUtc) LastUploadUtc = restored;
        }
        RejectedHash ??= _snapshot.RejectedHash;

        if (!InstalledLiveriesCache.IsResultUsable(_snapshot, profilePath, nowUtc)) return null;
        UploadedHash = _snapshot.Hash;
        return _snapshot;
    }

    public bool IsAlreadyHandled(string hash) => hash == UploadedHash || hash == RejectedHash;

    public TimeSpan TimeUntilUploadAllowed(DateTime nowUtc) =>
        LastUploadUtc + ServerSyncPolicy.MinUploadInterval - nowUtc;

    public void RecordUploadStarted(DateTime nowUtc)
    {
        LastUploadUtc = nowUtc;
        Persist(s => s with { LastUploadUtc = nowUtc });
    }

    public void RecordResult(string profilePath, string hash, IReadOnlyDictionary<string, int> installed, DateTime nowUtc)
    {
        UploadedHash = hash;
        SyncWanted = false;
        Persist(s => s with
        {
            ProfilePath = profilePath,
            Hash = hash,
            Installed = new Dictionary<string, int>(installed, StringComparer.OrdinalIgnoreCase),
            ResultAtUtc = nowUtc,
        });
    }

    public void RecordRejected(string? hash)
    {
        RejectedHash = hash;
        SyncWanted = false;
        Persist(s => s with { RejectedHash = hash });
    }

    public void BlockUploads(DateTime untilUtc) => BlockedUntilUtc = untilUtc;

    public void ForgetResult()
    {
        UploadedHash = null;
        RejectedHash = null;
    }

    public void Reset()
    {
        UploadedHash = null;
        RejectedHash = null;
        SyncWanted = false;
        BlockedUntilUtc = DateTime.MinValue;
        _snapshot = null;
        cache.Delete();
    }

    private void Persist(Func<InstalledLiveriesCache.Snapshot, InstalledLiveriesCache.Snapshot> update)
    {
        _snapshot = update(_snapshot ?? new InstalledLiveriesCache.Snapshot());
        cache.Save(_snapshot);
    }
}
