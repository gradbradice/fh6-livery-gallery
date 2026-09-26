using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchivedLiveryRowViewModel(ArchivedLiveryEntry entry) : ObservableObject, IThumbnailHost
{
    public ArchivedLiveryEntry Entry { get; } = entry;

    public string FolderName => Entry.Data.FolderName;
    public string LiveryName => Entry.Data.LiveryName;
    public string LiveryIdText => LiveryRestoreConflicts.FormatLiveryId(Entry.Data.LiveryId);
    public string AuthorRaw => Entry.Data.AuthorRaw;
    public string CarManufacturer => Entry.Data.CarManufacturerRaw;
    public string CarModelName => Entry.Data.CarModelNameRaw;
    public string? ThumbnailPath => Entry.Data.ThumbnailPath;

    public string ArchivedText => string.Format(Strings.ArchivedOnFormat, Entry.ArchivedAtUtc.ToLocalTime().ToString("d"));

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
