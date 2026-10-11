using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Views;

/// <summary>Asks the user for the save folder of the profile's game</summary>
internal sealed class SavePathPrompter(Window owner, GameProfile profile) : ISavePathPrompter
{
    public async Task<string?> PromptForSavePathAsync(bool initial, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return null;
        if (initial)
        {
            bool yes = await ConfirmDialog.AskAsync(owner, Strings.FolderNotFoundTitle,
                FolderNotFoundMessage(Strings.FolderNotFoundMessageFormat, profile.Game));
            if (!yes || ct.IsCancellationRequested) return null;
        }

        return await BrowseAsync(ct);
    }

    /// <summary>
    /// "The save folder of {0} was not found", with the usual path of the saves on its last line: the game id of the
    /// save folder goes in as {1}. A game whose saves are not in a PC GameSave folder has no such path: the line goes
    /// </summary>
    internal static string FolderNotFoundMessage(string format, ForzaGame game)
    {
        if (game.Saves.PcGameSaveGameId is { } id) return string.Format(format, game.Name, id);
        string text = format.TrimEnd();
        int lastLine = text.LastIndexOf('\n');
        if (lastLine >= 0 && text.AsSpan(lastLine).Contains("{1}", StringComparison.Ordinal)) text = text[..lastLine].TrimEnd();
        return string.Format(text, game.Name);
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

            if (!profile.Saves.IsValidSaveFolder(path))
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
