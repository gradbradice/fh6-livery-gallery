using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

internal sealed class SavePathPrompter(Window owner) : ISavePathPrompter
{
    public async Task<string?> PromptForSavePathAsync(bool initial)
    {
        if (initial)
        {
            bool yes = await ConfirmDialog.AskAsync(owner, Strings.FolderNotFoundTitle, Strings.FolderNotFoundMessage);
            if (!yes) return null;
        }

        return await BrowseAsync();
    }

    private async Task<string?> BrowseAsync()
    {
        var provider = owner.StorageProvider;
        if (provider is null) return null;

        while (true)
        {
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

                if (!retry) return null;
                continue;
            }

            return path;
        }
    }
}
