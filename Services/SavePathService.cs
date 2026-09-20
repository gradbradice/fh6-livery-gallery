using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Views;

namespace LiveryGallery.Services;

internal sealed class SavePathService(Window owner, AppSettingsData settings)
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
        int? hint = settings.LastKnownContainerSavePath == SavePath
            ? settings.LastKnownContainerId
            : null;

        var (path, containerId) = LocalSaveService.GetSaveDataPathWithId(SavePath, hint);
        if (containerId is not null
            && (containerId != settings.LastKnownContainerId || settings.LastKnownContainerSavePath != SavePath))
        {
            settings.LastKnownContainerId = containerId;
            settings.LastKnownContainerSavePath = SavePath;
            AppSettingsService.Save(settings);
        }
        return path;
    }

    public async Task<bool> PromptAsync(bool initial, Action onDeclined)
    {
        if (initial)
        {
            bool yes = await ConfirmDialog.AskAsync(owner, Strings.FolderNotFoundTitle, Strings.FolderNotFoundMessage);
            if (!yes)
            {
                onDeclined();
                return false;
            }
        }

        return await BrowseAsync();
    }

    public async Task<bool> BrowseAsync()
    {
        var provider = owner.StorageProvider;
        if (provider is null) return false;

        while (true)
        {
            var result = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Strings.SelectFolderDialogTitle,
                AllowMultiple = false
            });

            var folder = result.Count > 0 ? result[0] : null;
            string? path = folder?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return false;

            if (!LocalSaveService.IsSavePathValid(path))
            {
                bool retry = await ConfirmDialog.AskAsync(
                    owner,
                    Strings.SaveFolderValidationFailedTitle,
                    Strings.SaveFolderValidationFailed,
                    yesText: Strings.ButtonRetry,
                    noText: Strings.ButtonCancel);

                if (!retry) return false;
                continue;
            }

            SavePath = path;
            settings.SavePath = path;
            AppSettingsService.Save(settings);
            return true;
        }
    }

    public bool ShouldNotifyLost()
    {
        if (_lostNotified) return false;
        _lostNotified = true;
        return true;
    }

    public void ResetLostNotification() => _lostNotified = false;
}
