using Avalonia.Media.Imaging;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;

namespace LiveryGallery.ViewModels;

internal class LiveryEntry : INotifyPropertyChanged, IThumbnailHost
{
    public required LiveryData Data { get; init; }

    public string FolderName => Data.FolderName;
    public ulong LiveryId => Data.LiveryId;
    public string LiveryIdText => LiveryId > 0 ? $"#{LiveryId}" : "";
    public static bool ShowFolderNamesInTooltips { get; set; }
    public void RefreshDuplicateTooltip()
    {
        InvalidateDuplicateTexts();
        OnPropertyChanged(nameof(DuplicateMatchTooltip));
    }
    public string LiveryName => Data.DisplayLiveryName;
    public string AuthorRaw => Data.DisplayAuthorRaw;
    public string AuthorTooltip => Data.IsAuthorInferred ? Strings.AuthorInferredTooltip : Strings.AuthorLabel;
    public string? AuthorIdentityTagHex => Data.AuthorIdentityTagHex;
    public ulong? CreatorUserId => Data.CreatorUserId;
    public bool IsPossiblyGenerated => Data.IsPossiblyGenerated;
    public bool HasNoLayers => Data.HasNoLayers;
    public bool HasParseError => Data.HasParseError;
    public IReadOnlyList<LiveryParseIssue>? ParseIssues => Data.ParseIssues;
    public bool HasParseWarning => !HasParseError && Data.ParseIssues is { Count: > 0 };
    private string? _parseIssueTooltip;
    public string? ParseIssueTooltip => !HasParseError && Data.ParseIssues is not { Count: > 0 }
        ? null
        : _parseIssueTooltip ??= LiveryParseIssueFormatter.BuildTooltip(Data.ParseIssues, HasParseError);
    public int CarId => Data.CarId;

    public static string? View3DGameFolderProblem { get; set; } = Strings.View3DGameFolderChecking;

    private IEnumerable<LiveryParseIssue> CLiveryErrors => Data.ParseIssues?
        .Where(i => i.File == LiveryParseFile.CLivery && i.Severity == LiveryParseSeverity.Error) ?? [];

    private bool CanParseForView3D => Data.CLiveryHash is not null && !CLiveryErrors.Any();

    public bool CanView3D => View3DGameFolderProblem is null && CanParseForView3D;

    private string? _view3DTooltip;
    public string View3DTooltip => _view3DTooltip ??= BuildView3DTooltip();

    private string BuildView3DTooltip()
    {
        if (CanView3D) return $"{Strings.View3DTooltip}\n\n{Strings.View3DDifferenceNote}";

        var text = new StringBuilder();
        if (View3DGameFolderProblem is { } folderProblem) text.Append(folderProblem);
        if (!CanParseForView3D)
        {
            if (text.Length > 0) text.AppendLine().AppendLine();
            if (Data.CLiveryHash is null) text.Append(Strings.View3DNoCLivery);
            else
            {
                text.Append(Strings.View3DParseError);
                LiveryParseIssueFormatter.AppendIssueLines(text, CLiveryErrors);
            }
        }
        return text.ToString();
    }

    public void RefreshView3D()
    {
        _view3DTooltip = null;
        OnPropertyChanged(nameof(CanView3D));
        OnPropertyChanged(nameof(View3DTooltip));
    }
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
    public bool IsAuction => Data.IsAuction;

    public bool IsMine { get; set; }

    private int _installedCarCount;
    public int InstalledCarCount
    {
        get => _installedCarCount;
        set
        {
            if (_installedCarCount == value) return;
            _installedCarCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(InstalledBadgeTooltip));
        }
    }
    public bool IsInstalled => _installedCarCount > 0;
    public string InstalledBadgeTooltip => _installedCarCount > 1
        ? string.Format(Strings.InstalledBadgeTooltipManyFormat, _installedCarCount)
        : Strings.InstalledBadgeTooltip;
    private bool _showMineBadge;
    public bool ShowMineBadge
    {
        get => _showMineBadge;
        set
        {
            if (_showMineBadge == value) return;
            _showMineBadge = value;
            OnPropertyChanged();
        }
    }

    public string CarManufacturer => CarKnown
        ? CarManufacturerRaw
        : Strings.UnknownManufacturer;

    public string CarModelName => CarKnown
        ? CarModelNameRaw
        : string.Format(Strings.UnknownCarIdFormat, CarId);

    private string _author = string.Empty;
    public string Author
    {
        get => Data.IsAuthorUnknown ? Strings.UnknownAuthor : _author;
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

    private DuplicateStatus _duplicateStatus;
    public DuplicateStatus DuplicateStatus
    {
        get => _duplicateStatus;
        set
        {
            _duplicateStatus = value;
            InvalidateDuplicateTexts();
        }
    }
    public bool IsDuplicate => DuplicateStatus == DuplicateStatus.Duplicate;

    public static int PossibleDuplicateThresholdPercent { get; set; } = AppSettingsData.DefaultPossibleDuplicateThresholdPercent;

    public bool IsPossibleDuplicate => DuplicateStatus == DuplicateStatus.PossibleDuplicate && VisiblePossibleRelations is not null;

    private IReadOnlyList<DuplicateRelation>? _visiblePossibleRelations;
    private int _visiblePossibleRelationsThreshold = -1;

    private IReadOnlyList<DuplicateRelation>? VisiblePossibleRelations
    {
        get
        {
            if (_visiblePossibleRelationsThreshold != PossibleDuplicateThresholdPercent)
            {
                _visiblePossibleRelationsThreshold = PossibleDuplicateThresholdPercent;
                _visiblePossibleRelations = FilterByThreshold(PossibleDuplicateOf, PossibleDuplicateThresholdPercent);
            }
            return _visiblePossibleRelations;
        }
    }

    private static IReadOnlyList<DuplicateRelation>? FilterByThreshold(IReadOnlyList<DuplicateRelation>? relations, int thresholdPercent)
    {
        if (relations is not { Count: > 0 }) return [];
        var kept = relations.Where(r => r.Score is not { } score || score * 100 >= thresholdPercent - 1e-9).ToList();
        return kept.Count > 0 ? kept : null;
    }

    public void RefreshPossibleDuplicateThreshold()
    {
        InvalidateDuplicateTexts();
        OnPropertyChanged(nameof(IsPossibleDuplicate));
        OnPropertyChanged(nameof(PossibleDuplicateBadgeText));
        OnPropertyChanged(nameof(DuplicateMatchTooltip));
    }

    private IReadOnlyList<DuplicateRelation>? _possibleDuplicateOf;
    public IReadOnlyList<DuplicateRelation>? PossibleDuplicateOf
    {
        get => _possibleDuplicateOf;
        set
        {
            _possibleDuplicateOf = value;
            InvalidateDuplicateTexts();
        }
    }

    private string? _duplicateMatchTooltip;
    private string? _possibleDuplicateBadgeText;

    private void InvalidateDuplicateTexts()
    {
        _duplicateMatchTooltip = null;
        _possibleDuplicateBadgeText = null;
        _visiblePossibleRelationsThreshold = -1;
    }

    public string DuplicateMatchTooltip => _duplicateMatchTooltip ??= BuildDuplicateMatchTooltip();

    private string BuildDuplicateMatchTooltip()
    {
        bool isPossible = DuplicateStatus == DuplicateStatus.PossibleDuplicate;
        string baseText = isPossible
            ? Strings.PossibleDuplicateBadgeTooltip
            : Strings.DuplicateBadgeTooltip;

        string? scoresText = FormatScores(capAt9999: isPossible);
        return scoresText is null ? baseText : $"{baseText}\n{Strings.DuplicateMatchScoreHeader}\n{scoresText}";
    }

    public string PossibleDuplicateBadgeText => _possibleDuplicateBadgeText ??= BuildPossibleDuplicateBadgeText();

    private string BuildPossibleDuplicateBadgeText()
    {
        double? max = null;
        if (VisiblePossibleRelations is { Count: > 0 } relations)
            foreach (var r in relations)
                if (r.Score is { } s && (max is null || s > max)) max = s;

        if (max is not { } m) return Strings.PossibleDuplicateBadgeLabel;

        int truncated = (int)(m * 100);
        int capped = Math.Min(truncated, 99);
        return string.Format(Strings.PossibleDuplicateBadgeWithScoreFormat, capped.ToString(CultureInfo.InvariantCulture) + "%");
    }

    private string FormatRelationLabel(string folderName)
    {
        var ids = Data.RelatedLiveryIds;
        if (ids is null || !ids.TryGetValue(folderName, out ulong id)) return folderName;
        return ShowFolderNamesInTooltips ? $"#{id} ({folderName})" : $"#{id}";
    }

    private string? FormatScores(bool capAt9999)
    {
        var relations = DuplicateStatus == DuplicateStatus.PossibleDuplicate ? VisiblePossibleRelations : PossibleDuplicateOf;
        if (relations is not { Count: > 0 }) return null;
        var scored = relations
            .Where(r => r.Score.HasValue)
            .OrderByDescending(r => r.Score!.Value)
            .ToList();
        if (scored.Count == 0) return null;

        return string.Join("\n", scored.Select(r =>
        {
            double percent = r.Score!.Value * 100;
            if (capAt9999) percent = Math.Min(percent, 99.99);
            return string.Format(Strings.DuplicateMatchRelationFormat, FormatRelationLabel(r.Id), percent.ToString("0.00", CultureInfo.InvariantCulture));
        }));
    }

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
            _searchHaystack = IsAuction
                ? $"{CarManufacturer} {CarModelName} {CarYear} {LiveryName} {Author} {Strings.AuctionBadgeLabel}"
                : $"{CarManufacturer} {CarModelName} {CarYear} {LiveryName} {Author}";
            return _searchHaystack;
        }
    }

    public bool MatchesSearch(string[] tokens, bool includeFolderName)
    {
        if (tokens.Length == 0) return true;

        foreach (var token in tokens)
        {
            bool matches = SearchHaystack.Contains(token, StringComparison.OrdinalIgnoreCase)
                || (includeFolderName && FolderName.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (!matches) return false;
        }
        return true;
    }

    public void RefreshLocalizedText()
    {
        _searchHaystack = null;
        OnPropertyChanged(nameof(CarManufacturer));
        OnPropertyChanged(nameof(CarModelName));
        OnPropertyChanged(nameof(DateDisplay));
        _parseIssueTooltip = null;
        OnPropertyChanged(nameof(ParseIssueTooltip));
        InvalidateDuplicateTexts();
        OnPropertyChanged(nameof(DuplicateMatchTooltip));
        OnPropertyChanged(nameof(PossibleDuplicateBadgeText));
        OnPropertyChanged(nameof(AuthorTooltip));
        OnPropertyChanged(nameof(InstalledBadgeTooltip));
        OnPropertyChanged(nameof(LiveryName));
        OnPropertyChanged(nameof(Author));
        OnPropertyChanged(nameof(AuthorRaw));
        OnPropertyChanged(nameof(AuthorDisplayText));
        RefreshView3D();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
