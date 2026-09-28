using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal sealed class SavePathPrompter(Window owner) : ISavePathPrompter
{
    public async Task<string?> PromptForSavePathAsync(bool initial, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return null;
        if (initial)
        {
            bool yes = await ConfirmDialog.AskAsync(owner, Strings.FolderNotFoundTitle, Strings.FolderNotFoundMessage);
            if (!yes || ct.IsCancellationRequested) return null;
        }

        return await BrowseAsync(ct);
    }

    private async Task<string?> BrowseAsync(CancellationToken ct)
    {
        var provider = owner.StorageProvider;
        if (provider is null) return null;

        while (true)
        {
            if (ct.IsCancellationRequested) return null;
            var result = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Strings.SelectFolderDialogTitle,
                AllowMultiple = false
            });

            var folder = result.Count > 0 ? result[0] : null;
            string? path = folder?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return null;

            if (!LocalSaveService.IsSavePathValid(path))
            {
                bool retry = await ConfirmDialog.AskAsync(
                    owner,
                    Strings.SaveFolderValidationFailedTitle,
                    Strings.SaveFolderValidationFailed,
                    yesText: Strings.ButtonRetry,
                    noText: Strings.ButtonCancel);

                if (!retry || ct.IsCancellationRequested) return null;
                continue;
            }

            return ct.IsCancellationRequested ? null : path;
        }
    }
}
