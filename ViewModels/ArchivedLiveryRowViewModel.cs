using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Models;

namespace LiveryGallery.ViewModels;

internal sealed partial class ArchivedLiveryRowViewModel(ArchivedLiveryEntry entry) : ObservableObject
{
    public ArchivedLiveryEntry Entry { get; } = entry;

    public string FolderName => Entry.Data.FolderName;
    public string LiveryName => Entry.Data.LiveryName;
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
