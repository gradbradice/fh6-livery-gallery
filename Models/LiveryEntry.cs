using Avalonia.Media.Imaging;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveryGallery.Models;

internal class LiveryEntry : INotifyPropertyChanged
{
    public required string FolderPath { get; init; }
    public required string FolderName { get; init; }
    public required string LiveryName { get; init; }
    public required string Author { get; init; }
    public required int CarId { get; init; }
    public required string CarManufacturerRaw { get; init; }
    public required string CarModelNameRaw { get; init; }
    public int? CarYear { get; init; }
    public bool CarKnown { get; init; }
    public int? CreatedYear { get; init; }
    public int? CreatedMonth { get; init; }
    public DateTime? DownloadDate { get; init; }
    public string? ThumbnailPath { get; init; }

    public string CarManufacturer => CarKnown
        ? CarManufacturerRaw
        : Strings.UnknownManufacturer;

    public string CarModelName => CarKnown
        ? CarModelNameRaw
        : string.Format(Strings.UnknownCarIdFormat, CarId);

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    private List<string> _tags = [];
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        set
        {
            _tags = [.. value];
            OnPropertyChanged();
        }
    }

    public DateTime? DownloadYearMonth => DownloadDate is { } d ? new DateTime(d.Year, d.Month, 1) : null;

    public string? CLiveryHash { get; init; }
    public IReadOnlyList<uint>? SectionCounts { get; init; }
    public DuplicateStatus DuplicateStatus { get; set; }
    public bool IsDuplicate => DuplicateStatus == DuplicateStatus.Duplicate;
    public bool IsPossibleDuplicate => DuplicateStatus == DuplicateStatus.PossibleDuplicate;
    public required bool HasThumbnail { get; init; }

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail == value) return;
            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowThumbnailImage));
        }
    }

    public bool ShowThumbnailImage => HasThumbnail && Thumbnail is not null;

    public string DateDisplay
    {
        get
        {
            if (DownloadDate is { } d) return d.ToString("d", AppLocalisationService.Culture);
            if (CreatedYear is > 0 && CreatedMonth is >= 1 and <= 12)
                return new DateTime(CreatedYear.Value, CreatedMonth.Value, 1)
                    .ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture);
            return "—";
        }
    }

    private string? _searchHaystack;
    private string SearchHaystack
    {
        get
        {
            if (_searchHaystack is not null) return _searchHaystack;
            _searchHaystack = $"{CarManufacturer} {CarModelName} {CarYear} {LiveryName} {Author}";
            return _searchHaystack;
        }
    }

    public bool MatchesSearch(string[] tokens)
    {
        if (tokens.Length == 0) return true;

        foreach (var token in tokens)
        {
            if (!SearchHaystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public void RefreshLocalizedText()
    {
        _searchHaystack = null;
        OnPropertyChanged(nameof(CarManufacturer));
        OnPropertyChanged(nameof(CarModelName));
        OnPropertyChanged(nameof(DateDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
