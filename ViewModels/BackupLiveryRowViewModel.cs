using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Configuration;
using LiveryGallery.Models;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class BackupLiveryRowViewModel : ObservableObject, IThumbnailHost
{
    private BackupLiveryRowViewModel(LiveryData data, string? thumbnailPath)
    {
        Data = data;
        ThumbnailPath = thumbnailPath;
    }

    public static BackupLiveryRowViewModel FromBackup(LiveryData data, string? previewPath = null) =>
        new(data, previewPath ?? LiveryBackupService.FindExistingPreview(data.ThumbnailPath));

    public bool NeedsPreview => ThumbnailPath is null && !string.IsNullOrEmpty(Data.ThumbnailPath);
    public static BackupLiveryRowViewModel FromCurrent(LiveryData data) =>
        new(data, data.ThumbnailPath is { Length: > 0 } path ? path : null);

    public LiveryData Data { get; }

    public string FolderName => Data.FolderName;
    public string LiveryName => Data.LiveryName;
    public string LiveryIdText => LiveryRestoreConflicts.FormatLiveryId(Data.LiveryId);
    public string AuthorRaw => Data.AuthorRaw;
    public string CarManufacturer => Data.CarManufacturerRaw;
    public string CarModelName => Data.CarModelNameRaw;
    public string? ThumbnailPath { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
