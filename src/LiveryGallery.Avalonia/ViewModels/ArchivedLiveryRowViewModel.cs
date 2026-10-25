using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

/// <param name="showFolderName">
/// The setting "show folder names and search by them": the row shows the folder under its date, as a gallery card does,
/// the number's tooltip names it, and search looks at it
/// </param>
internal sealed partial class ArchivedLiveryRowViewModel(ArchivedLiveryEntry entry, AccountContext accounts, bool showFolderName = false)
    : ObservableObject, IThumbnailHost
{
    public ArchivedLiveryEntry Entry { get; } = entry;

    /// <summary>Only another account is shown: on rows of the open account the note would be noise</summary>
    public bool IsFromOtherAccount { get; } = accounts.IsOther(entry.SourceAccount);

    public string OtherAccountText { get; } = accounts.SourceNote(entry.SourceAccount);

    public string FolderName => Entry.Data.FolderName;
    public bool ShowFolderName { get; } = showFolderName;
    public string LiveryName => Entry.Data.DisplayLiveryName;
    public string LiveryIdText => LiveryRestoreConflicts.FormatLiveryId(Entry.Data.LiveryId);

    /// <summary>As on a gallery card: the number, and the folder when folder names are shown</summary>
    public string? LiveryIdTooltip => ShowFolderName && LiveryIdText.Length > 0 ? $"{LiveryIdText} ({FolderName})" : null;
    public string AuthorRaw => Entry.Data.DisplayAuthorRaw;
    public string CarManufacturer => Entry.Data.CarManufacturerRaw;
    public string CarModelName => Entry.Data.CarModelNameRaw;
    public string? ThumbnailPath => Entry.Data.ThumbnailPath;

    private string? _searchHaystack;

    // What the row shows, as the gallery's search looks at a card: car, year, name, author, and the number
    private string SearchHaystack => _searchHaystack ??=
        $"{CarManufacturer} {CarModelName} {Entry.Data.CarYear} {LiveryName} {AuthorRaw} {LiveryIdText}";

    /// <summary>Every token is in the row's texts (or, with folder names shown, in the folder name), ignoring case</summary>
    public bool MatchesSearch(string[] tokens)
    {
        foreach (string token in tokens)
        {
            bool matches = SearchHaystack.Contains(token, StringComparison.OrdinalIgnoreCase)
                || (ShowFolderName && FolderName.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (!matches) return false;
        }
        return true;
    }

    public string ArchivedText => string.Format(Strings.ArchivedOnFormat, Entry.ArchivedAtUtc.ToLocalTime().ToString("d"));

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
