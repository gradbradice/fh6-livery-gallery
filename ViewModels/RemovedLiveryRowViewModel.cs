using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Configuration;
using LiveryGallery.Models;

namespace LiveryGallery.ViewModels;

internal sealed partial class RemovedLiveryRowViewModel(LiveryData data) : ObservableObject
{
    public LiveryData Data { get; } = data;

    public string FolderName => Data.FolderName;
    public string LiveryName => Data.LiveryName;
    public string AuthorRaw => Data.AuthorRaw;
    public string CarManufacturer => Data.CarManufacturerRaw;
    public string CarModelName => Data.CarModelNameRaw;

    public string? ThumbnailPath => Data.ThumbnailPath is { Length: > 0 } fileName
        ? Path.Combine(AppSettings.ThumbsPath, fileName)
        : null;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
