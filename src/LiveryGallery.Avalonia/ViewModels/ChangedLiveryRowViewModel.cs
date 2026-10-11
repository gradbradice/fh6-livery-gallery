using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveryGallery.ViewModels;

/// <summary>One card in the "changes since the last launch" dialog. Texts are taken once, when the dialog opens</summary>
internal sealed partial class ChangedLiveryRowViewModel : ObservableObject, IThumbnailHost
{
    public ChangedLiveryRowViewModel(LiveryEntry entry, string? thumbnailPath, bool isRemoved)
    {
        LiveryName = entry.LiveryName;
        LiveryIdText = entry.LiveryIdText;
        Author = entry.AuthorDisplayText;
        CarText = entry.CarYear is { } year
            ? $"{entry.CarManufacturer} {entry.CarModelName} ({year})"
            : $"{entry.CarManufacturer} {entry.CarModelName}";
        DateText = entry.DateDisplay;
        IsAuction = entry.IsAuction;
        IsMine = entry.IsMine;
        IsRemoved = isRemoved;
        ThumbnailPath = thumbnailPath;
    }

    public string LiveryName { get; }
    public string LiveryIdText { get; }
    public string Author { get; }
    public string CarText { get; }
    public string DateText { get; }
    public bool IsAuction { get; }
    public bool IsMine { get; }
    public bool IsRemoved { get; }

    /// <summary>Removed liveries are dimmed: they are no longer in the save folder</summary>
    public double ThumbnailOpacity => IsRemoved ? 0.55 : 1.0;

    public string? ThumbnailPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private Bitmap? _thumbnail;

    public bool ShowPlaceholder => Thumbnail is null;
}
