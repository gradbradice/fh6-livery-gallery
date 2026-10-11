using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using LiveryGallery.Services.Accounts;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchivedLiveryRowViewModel(ArchivedLiveryEntry entry, AccountContext accounts)
    : ObservableObject, IThumbnailHost
{
    public ArchivedLiveryEntry Entry { get; } = entry;

    /// <summary>Only another account is shown: on rows of the open account the note would be noise</summary>
    public bool IsFromOtherAccount { get; } = accounts.IsOther(entry.SourceAccount);

    public string OtherAccountText { get; } = accounts.SourceNote(entry.SourceAccount);

    public string FolderName => Entry.Data.FolderName;
    public string LiveryName => Entry.Data.DisplayLiveryName;
    public string LiveryIdText => LiveryRestoreConflicts.FormatLiveryId(Entry.Data.LiveryId);
    public string AuthorRaw => Entry.Data.DisplayAuthorRaw;
    public string CarManufacturer => Entry.Data.CarManufacturerRaw;
    public string CarModelName => Entry.Data.CarModelNameRaw;
    public string? ThumbnailPath => Entry.Data.ThumbnailPath;

    public string ArchivedText => string.Format(Strings.ArchivedOnFormat, Entry.ArchivedAtUtc.ToLocalTime().ToString("d"));

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
