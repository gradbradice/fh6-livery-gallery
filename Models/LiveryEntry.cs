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
    public List<string> Tags
    {
        get => _tags;
        set
        {
            _tags = value;
            OnPropertyChanged();
        }
    }
    public DateTime? DownloadYearMonth => DownloadDate is { } d ? new DateTime(d.Year, d.Month, 1) : null;

    public string? CLiveryHash { get; init; }
    public IReadOnlyList<uint>? SectionCounts { get; init; }
    public DuplicateStatus DuplicateStatus { get; set; }
    public bool IsDuplicate => DuplicateStatus == DuplicateStatus.Duplicate;
    public bool IsPossibleDuplicate => DuplicateStatus == DuplicateStatus.PossibleDuplicate;

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath);

    private Bitmap? _thumbnail;
    private bool _thumbnailLoaded;

    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;

            if (string.IsNullOrEmpty(ThumbnailPath) || !File.Exists(ThumbnailPath))
                return _thumbnail = null;

            try
            {
                using var stream = File.OpenRead(ThumbnailPath);
                return _thumbnail = new Bitmap(stream);
            }
            catch
            {
                return _thumbnail = null;
            }
        }
    }

    public string DateDisplay
    {
        get
        {
            if (DownloadDate is { } d) return d.ToString("dd.MM.yyyy", AppLocalisationService.Culture);
            if (CreatedYear is > 0 && CreatedMonth is >= 1 and <= 12)
                return new DateTime(CreatedYear.Value, CreatedMonth.Value, 1)
                    .ToString(AppLocalisationService.MonthYearFormat, AppLocalisationService.Culture);
            return "—";
        }
    }

    private string SearchHaystack =>
        $"{CarManufacturer} {CarModelName} {CarYear} {LiveryName} {Author}".ToLowerInvariant();

    public bool MatchesSearch(string term)
    {
        var tokens = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;

        foreach (var token in tokens)
        {
            if (!SearchHaystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
