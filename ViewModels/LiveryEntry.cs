using Avalonia.Media.Imaging;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveryGallery.ViewModels;

internal class LiveryEntry : INotifyPropertyChanged
{
    public required LiveryData Data { get; init; }

    public string FolderName => Data.FolderName;
    public string LiveryName => Data.LiveryName;
    public string AuthorRaw => Data.AuthorRaw;
    public string? AuthorIdentityTagHex => Data.AuthorIdentityTagHex;
    public ulong? CreatorUserId => Data.CreatorUserId;
    public bool IsPossiblyGenerated => Data.IsPossiblyGenerated;
    public int CarId => Data.CarId;
    public string CarManufacturerRaw => Data.CarManufacturerRaw;
    public string CarModelNameRaw => Data.CarModelNameRaw;
    public int? CarYear => Data.CarYear;
    public bool CarKnown => Data.CarKnown;
    public int? CreatedYear => Data.CreatedYear;
    public int? CreatedMonth => Data.CreatedMonth;
    public DateTime? DownloadDate => Data.DownloadDate;
    public string? ThumbnailPath => Data.ThumbnailPath;
    public string? CLiveryHash => Data.CLiveryHash;
    public bool HasThumbnail => Data.HasThumbnail;

    public bool IsMine { get; set; }
    public bool ShowMineBadge { get; set; }

    public string CarManufacturer => CarKnown
        ? CarManufacturerRaw
        : Strings.UnknownManufacturer;

    public string CarModelName => CarKnown
        ? CarModelNameRaw
        : string.Format(Strings.UnknownCarIdFormat, CarId);

    private string _author = string.Empty;
    public string Author
    {
        get => _author;
        set
        {
            if (_author == value) return;
            _author = value;
            _searchHaystack = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AuthorDisplayText));
        }
    }
    public string AuthorDisplayText => string.Equals(Author, AuthorRaw, StringComparison.OrdinalIgnoreCase)
        ? Author
        : $"{Author} ({AuthorRaw})";

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

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private List<string> _tags = [];
    private System.Collections.ObjectModel.ReadOnlyCollection<string> _tagsReadOnly = new([]);
    private HashSet<string> _tagsSet = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Tags
    {
        get => _tagsReadOnly;
        set
        {
            _tags = [.. value];
            _tagsReadOnly = _tags.AsReadOnly();
            _tagsSet = new HashSet<string>(_tags, StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged();
        }
    }

    public IReadOnlySet<string> TagsSet => _tagsSet;

    public DateTime? DownloadYearMonth => Data.DownloadDate is { } d ? new DateTime(d.Year, d.Month, 1) : null;

    public DuplicateStatus DuplicateStatus { get; set; }
    public bool IsDuplicate => DuplicateStatus == DuplicateStatus.Duplicate;
    public bool IsPossibleDuplicate => DuplicateStatus == DuplicateStatus.PossibleDuplicate;
    public IReadOnlyList<string>? PossibleDuplicateOf { get; set; }

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
            if (Data.DownloadDate is { } d) return d.ToString("d", AppLocalisationService.Culture);
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
