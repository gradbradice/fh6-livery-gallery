using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal sealed class SavePathService(AppSettingsData settings)
{
    public string? SavePath { get; private set; }
    private bool _lostNotified;

    public SaveIdentity? CurrentIdentity
    {
        get
        {
            if (SavePath is null)
            {
                AppLogger.LogErrorThrottled("CurrentIdentity.NoSavePath",
                    "SavePathService.CurrentIdentity: SavePath has not been set yet. The current UserId cannot be determined.",
                    new InvalidOperationException("SavePath is null"));
                return null;
            }

            int? containerId = settings.LastKnownContainerSavePath == SavePath ? settings.LastKnownContainerId : null;
            var identity = LocalSaveService.ResolveSaveIdentity(SavePath, containerId);
            if (identity is null)
            {
                AppLogger.LogErrorThrottled("CurrentIdentity.Unresolved",
                    $"SavePathService.CurrentIdentity: Failed to parse UserId from either the SavePath folder name ('{Path.GetFileName(SavePath)}') or the manifest. " +
                    $"The MINE badge will not be shown for any livery.",
                    new InvalidOperationException("ResolveSaveIdentity returned null"));
            }
            return identity;
        }
    }

    public ulong? CurrentUserId => CurrentIdentity?.UserId;

    public bool WasSavedPathMissing =>
        !string.IsNullOrWhiteSpace(settings.SavePath) && !Directory.Exists(settings.SavePath);

    public void InitializeFromSettings()
    {
        SavePath = !string.IsNullOrWhiteSpace(settings.SavePath) && Directory.Exists(settings.SavePath)
            ? settings.SavePath
            : LocalSaveService.FindLocalSavePath();
    }

    public void SyncFromSettings() => SavePath = settings.SavePath;

    public string? ResolveSaveDataPath()
    {
        if (SavePath is null) return null;

        var (path, containerId) = LocalSaveService.GetSaveDataPathWithId(SavePath);
        if (containerId is not null
            && (containerId != settings.LastKnownContainerId || settings.LastKnownContainerSavePath != SavePath))
        {
            settings.LastKnownContainerId = containerId;
            settings.LastKnownContainerSavePath = SavePath;
            AppSettingsService.Save(settings);
        }
        return path;
    }

    public void SetSavePath(string path)
    {
        SavePath = path;
        settings.SavePath = path;
        AppSettingsService.Save(settings);
    }

    public bool ShouldNotifyLost()
    {
        if (_lostNotified) return false;
        _lostNotified = true;
        return true;
    }

    public void ResetLostNotification() => _lostNotified = false;
}