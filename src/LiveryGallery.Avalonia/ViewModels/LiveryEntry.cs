using Avalonia.Media.Imaging;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;

namespace LiveryGallery.ViewModels;

internal class LiveryEntry : INotifyPropertyChanged, IThumbnailHost, IGalleryItem
{
    public required LiveryData Data { get; init; }

    public string FolderName => Data.FolderName;
    public ulong LiveryId => Data.LiveryId;
    public string LiveryIdText => LiveryId > 0 ? $"#{LiveryId}" : "";
    public static bool ShowFolderNamesInTooltips { get; set; }

    /// <summary>The game has a livery menu grid (the profile): the "Find in game" button and menu item are shown</summary>
    public static bool IsFindInGameAvailable { get; set; } = true;

    /// <summary>The card's "Find in game" button is shown (disabled with a reason for an auction livery)</summary>
    public bool HasFindInGameButton => IsFindInGameAvailable;

    /// <summary>The menu item is offered: the game has a grid and the livery is not an auction one</summary>
    public bool ShowFindInGame => IsFindInGameAvailable && !IsAuction;
    public void RefreshDuplicateTooltip()
    {
        InvalidateDuplicateTexts();
        OnPropertyChanged(nameof(DuplicateMatchTooltip));
    }
    public string LiveryName => Data.DisplayLiveryName;
    public string AuthorRaw => Data.DisplayAuthorRaw;
    /// <summary>Unknown only when nobody, not even the user (<see cref="IsAuthorAssigned"/>), tells</summary>
    public bool IsAuthorUnknown => Data.IsAuthorUnknown && !IsAuthorAssigned;
    public string AuthorTooltip
    {
        get
        {
            string main = IsAuthorAssigned ? Strings.AuthorAssignedTooltip
                : IsAuthorIdInvalid ? Strings.AuthorIdInvalidTooltip
                : IsAuthorIdMissing ? Strings.AuthorIdMissingTooltip
                : Data.IsAuthorUnknown && Data.AuthorIdKind == AuthorIdKind.None ? Strings.AuthorIdNoneTooltip
                : IsOwnUnpublished ? Strings.AuthorOwnUnpublishedTooltip
                : Data.IsAuthorInferred ? Strings.AuthorInferredTooltip
                : Strings.AuthorLabel;
            if (AuthorDetails is { } details) main += "\n\n" + AuthorContext.DescribeDetails(details);
            return AuthorHintsText is { } hints ? $"{main}\n\n{hints}" : main;
        }
    }

    /// <summary>The card the user assigned this livery to (its own author id says nothing); null when none</summary>
    public string? AssignedCardId { get; init; }

    private readonly string? _authorAccountTag;
    private readonly bool _authorAccountTagSet;
    /// <summary>
    /// The author's account: the livery's own id, or the shown save's account for its own liveries without an id
    /// (<see cref="IsOwnUnpublished"/>). <see cref="AuthorIdentityTagHex"/> reads it
    /// </summary>
    public string? AuthorAccountTag
    {
        get => _authorAccountTagSet ? _authorAccountTag : Data.AuthorIdentityTagHex;
        init
        {
            _authorAccountTag = value;
            _authorAccountTagSet = true;
        }
    }

    private readonly string? _authorKey;
    private readonly bool _authorKeySet;
    /// <summary>Who the author is (<see cref="AuthorKeys"/>); by default the shown name's key</summary>
    public string? AuthorKey
    {
        get => _authorKeySet ? _authorKey : IsAuthorUnknown ? null : AuthorKeys.ForName(Author);
        init
        {
            _authorKey = value;
            _authorKeySet = true;
        }
    }

    /// <summary>How the author is told from others of its name (<see cref="Author"/> is it, worded); null when not known</summary>
    public AuthorLabel? AuthorLabel { get; init; }

    /// <summary>The author's nicknames, liveries and earliest livery, for an author whose name another one shares; null otherwise</summary>
    public AuthorDetails? AuthorDetails { get; init; }

    /// <summary>Made on this PC and not published: no author id, but the shown save's account's nickname</summary>
    public bool IsOwnUnpublished { get; init; }

    /// <summary>The user said whose livery this is (<see cref="AuthorCardService.AssignAsync"/>)</summary>
    public bool IsAuthorAssigned => AssignedCardId is not null;

    /// <summary>The author id is not an account (an edited header) and the user has not said whose livery it is</summary>
    public bool IsAuthorIdInvalid => Data.AuthorIdKind == AuthorIdKind.Invalid && !IsAuthorAssigned;

    /// <summary>Nothing but the user can tell the author: the id is invalid or zeros. Also when already assigned (to change it)</summary>
    public bool CanAssignAuthor => Data.AuthorIdKind != AuthorIdKind.Account && !IsOwnUnpublished;

    /// <summary>The author row shows a warning: an invalid id, or hints that nobody answered yet</summary>
    public bool ShowAuthorWarning => IsAuthorIdInvalid || IsAuthorIdMissing || HasAuthorHints;

    /// <summary>
    /// A name without an author id (zeros), not the user's own unpublished livery and not assigned: the name may be
    /// anyone's, so the card marks it as it marks an edited id. A livery with neither name nor id shows "unknown author"
    /// and needs no mark
    /// </summary>
    public bool IsAuthorIdMissing =>
        Data.AuthorIdKind == AuthorIdKind.None && !Data.IsAuthorUnknown && !IsOwnUnpublished && !IsAuthorAssigned;

    private ArtworkSketch? _sketch;
    private bool _sketchRead;
    /// <summary>The fingerprint of the artwork, read once from <see cref="LiveryData.ArtworkSketch"/>; null when there is none</summary>
    public ArtworkSketch? Sketch
    {
        get
        {
            if (!_sketchRead)
            {
                _sketch = ArtworkSketch.FromBase64(Data.ArtworkSketch);
                _sketchRead = true;
            }
            return _sketch;
        }
    }

    private IReadOnlyList<AuthorHintView> _authorHints = [];
    /// <summary>Who this livery may be by, when nothing else tells (<see cref="AuthorHints"/>); best first</summary>
    public IReadOnlyList<AuthorHintView> AuthorHints
    {
        get => _authorHints;
        set
        {
            if (_authorHints.SequenceEqual(value)) return;
            _authorHints = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAuthorHints));
            OnPropertyChanged(nameof(ShowAuthorWarning));
            OnPropertyChanged(nameof(AuthorTooltip));
        }
    }

    public bool HasAuthorHints => _authorHints.Count > 0;

    private string? AuthorHintsText => _authorHints.Count == 0
        ? null
        : Strings.AuthorHintsTooltipHeader + "\n" + string.Join("\n", _authorHints.Select(h => "• " + h.Text));

    /// <summary>The author's account (<see cref="AuthorAccountTag"/>); the livery's own id is <see cref="LiveryData.AuthorIdentityTagHex"/></summary>
    public string? AuthorIdentityTagHex => AuthorAccountTag;
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
        .Where(i => i.File == LiveryParseFile.Livery && i.Severity == LiveryParseSeverity.Error) ?? [];

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

    /// <summary>The month the livery was made, as its header says (<see cref="IGalleryItem.CreatedOn"/>)</summary>
    public DateOnly? CreatedOn => CreatedYear is > 1999 and < 2200 && CreatedMonth is >= 1 and <= 12
        ? new DateOnly(CreatedYear.Value, CreatedMonth.Value, 1)
        : null;

    /// <summary>How many shapes the livery holds (<see cref="IGalleryItem.LayerCount"/>)</summary>
    public int? LayerCount => Data.LayerCount;
    public DateTime? DownloadDate => Data.DownloadDate;
    /// <summary>The livery's preview: the gallery's PNG of the game's one, or the one it drew (<see cref="LiveryData.ThumbnailDrawn"/>)</summary>
    public string? ThumbnailPath => Data.ThumbnailPath;
    public string? CLiveryHash => Data.CLiveryHash;
    public bool HasThumbnail => Data.HasThumbnail;

    /// <summary>
    /// The livery has no preview yet, and the gallery can draw one from the 3D model (<see cref="RenderedThumbnails"/>):
    /// the livery file is there and readable, the car is known
    /// </summary>
    public bool CanRenderThumbnail => !HasThumbnail && Data.ThumbnailPath is null && CanParseForView3D && CarId > 0;

    private string? _renderedThumbnailPath;
    /// <summary>
    /// The preview the gallery has just drawn, shown until the next scan makes it the livery's preview
    /// (<see cref="ThumbnailPath"/>); null when none was drawn
    /// </summary>
    public string? RenderedThumbnailPath
    {
        get => _renderedThumbnailPath;
        set
        {
            if (_renderedThumbnailPath == value) return;
            _renderedThumbnailPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailTooltip));
        }
    }

    /// <summary>The livery's preview; the one just drawn until a scan attaches it</summary>
    string? IThumbnailHost.ThumbnailPath => Data.ThumbnailPath ?? (CanRenderThumbnail ? RenderedThumbnailPath : null);

    /// <summary>On the card's picture: it was drawn by the gallery, not taken from the save</summary>
    public string? ThumbnailTooltip => Data.ThumbnailDrawn || (Data.ThumbnailPath is null && RenderedThumbnailPath is not null)
        ? Strings.RenderedThumbnailTooltip
        : null;
    /// <summary>A livery the game only lists (the auction house); the game profile tells</summary>
    public bool IsAuction { get; init; }

    private GridSlot? _gamePosition;
    /// <summary>Place in the in-game "My Designs" grid; null for auction liveries or when it is not known</summary>
    public GridSlot? GamePosition
    {
        get => _gamePosition;
        set
        {
            if (Nullable.Equals(_gamePosition, value)) return;
            _gamePosition = value;
            OnPropertyChanged();
            OnGamePositionTextsChanged();
        }
    }

    private bool _selectionBlocksNavigation;
    /// <summary>Several liveries are selected: "Find in game" is only offered when the target is unambiguous</summary>
    public bool SelectionBlocksNavigation
    {
        get => _selectionBlocksNavigation;
        set
        {
            if (_selectionBlocksNavigation == value) return;
            _selectionBlocksNavigation = value;
            OnPropertyChanged(nameof(CanNavigateInGame));
            OnPropertyChanged(nameof(NavigateInGameTooltip));
        }
    }

    private void OnGamePositionTextsChanged()
    {
        OnPropertyChanged(nameof(HasGamePosition));
        OnPropertyChanged(nameof(GamePositionText));
        OnPropertyChanged(nameof(GamePositionTooltip));
        OnPropertyChanged(nameof(CanNavigateInGame));
        OnPropertyChanged(nameof(NavigateInGameTooltip));
    }

    public bool HasGamePosition => _gamePosition is not null;
    public int? GameSlot => _gamePosition?.Slot;
    public string GamePositionText => _gamePosition?.Label ?? "";
    public bool CanNavigateInGame => !IsAuction && _gamePosition is not null && !_selectionBlocksNavigation;

    private string GamePositionDescription => _gamePosition is { } p
        ? string.Format(Strings.GamePositionDescriptionFormat, p.Column, p.Row, p.Rows, p.Slot, p.TotalSlots)
        : "";

    public string? GamePositionTooltip => _gamePosition is null
        ? null
        : string.Format(Strings.GamePositionTooltipFormat, GamePositionDescription);

    public string NavigateInGameTooltip =>
        IsAuction ? Strings.NavigateInGameAuctionTooltip
        : _gamePosition is null ? Strings.NavigateInGameNoPositionTooltip
        : _selectionBlocksNavigation ? Strings.NavigateInGameMultipleSelectedTooltip
        : string.Format(Strings.NavigateInGameTooltipFormat, GamePositionDescription);

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

    /// <summary>
    /// The car's texts in the chosen game language (make, model, country, car type) by car id; set at startup.
    /// Null, or a null result: the car list's own texts
    /// </summary>
    public static Func<int, CarTextResult?>? CarTextSource { get; set; }

    private CarTextResult? _carTexts;
    private bool _carTextsResolved;
    private CarTextResult? LocalizedCar
    {
        get
        {
            if (!_carTextsResolved)
            {
                _carTexts = CarKnown ? CarTextSource?.Invoke(CarId) : null;
                _carTextsResolved = true;
            }
            return _carTexts;
        }
    }

    public string CarManufacturer => CarKnown
        ? LocalizedCar?.Texts.Manufacturer ?? CarManufacturerRaw
        : Strings.UnknownManufacturer;

    public string CarModelName => CarKnown
        ? LocalizedCar?.Texts.Name ?? CarModelNameRaw
        : string.Format(Strings.UnknownCarIdFormat, CarId);

    /// <summary>The country of the car's make in the chosen game language; empty when unknown</summary>
    public string CarCountry => LocalizedCar?.Texts.Country ?? "";

    /// <summary>The country from the car database (English): what the country quick filter matches</summary>
    public string CarCountryRaw => LocalizedCar?.CountryRaw ?? "";

    /// <summary>The car's group in the game (Modern Sports Cars) in the chosen game language; empty when unknown</summary>
    public string CarType => LocalizedCar?.Texts.CarType ?? "";

    /// <summary>The car type from the car database (English): what the car type quick filter matches</summary>
    public string CarTypeRaw => LocalizedCar?.CarTypeRaw ?? "";

    public bool HasCarCountry => CarCountry.Length > 0;
    public bool HasCarType => CarType.Length > 0;
    public bool HasCarDetails => HasCarCountry || HasCarType;

    /// <summary>"Country of the manufacturer: Italy", and why it is not in the chosen language when it is not</summary>
    public string CarCountryTooltip => WithNote($"{Strings.CarCountryLabel}: {CarCountry}");

    /// <summary>"Car type in the game: Modern Sports Cars", and why it is not in the chosen language when it is not</summary>
    public string CarTypeTooltip => WithNote($"{Strings.CarTypeLabel}: {CarType}");

    private string WithNote(string text) =>
        LocalizedCar?.UnavailableNote is { } note ? $"{text}{Environment.NewLine}{Environment.NewLine}{note}" : text;

    /// <summary>Takes the car's texts again: the language or the texts from the game changed</summary>
    public void RefreshCarTexts()
    {
        _carTextsResolved = false;
        _searchHaystack = null;
        OnPropertyChanged(nameof(CarManufacturer));
        OnPropertyChanged(nameof(CarModelName));
        OnPropertyChanged(nameof(CarCountry));
        OnPropertyChanged(nameof(CarType));
        OnPropertyChanged(nameof(CarCountryRaw));
        OnPropertyChanged(nameof(CarTypeRaw));
        OnPropertyChanged(nameof(CarCountryTooltip));
        OnPropertyChanged(nameof(CarTypeTooltip));
        OnPropertyChanged(nameof(HasCarCountry));
        OnPropertyChanged(nameof(HasCarType));
        OnPropertyChanged(nameof(HasCarDetails));
    }

    private string _author = string.Empty;
    public string Author
    {
        get => Data.IsAuthorUnknown && !IsAuthorAssigned ? Strings.UnknownAuthor : _author;
        set
        {
            if (_author == value) return;
            _author = value;
            _searchHaystack = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AuthorDisplayText));
        }
    }
    /// <summary>
    /// The author, and the livery's own nickname after it when the author is shown under another name (a card's, or the
    /// account's latest); not when the label already names it ("Ann (formerly Old)")
    /// </summary>
    public string AuthorDisplayText => string.Equals(AuthorLabel?.Name ?? Author, AuthorRaw, StringComparison.OrdinalIgnoreCase)
        || string.Equals(AuthorLabel?.FormerName, AuthorRaw, StringComparison.OrdinalIgnoreCase)
        || (IsAuthorAssigned && (Data.IsAuthorUnknown || AuthorAccounts.IsBlankName(Data.AuthorRaw)))
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

    private bool _isLocked;
    /// <summary>Locked by the user: kept out of the archive</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockTooltip));
        }
    }

    public string LockTooltip => IsLocked ? Strings.LockedTooltip : Strings.LockToggleTooltip;

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
            OnPropertyChanged(nameof(HasTags));
        }
    }

    /// <summary>The card shows the row of tags only when there are any: an empty row would add its gap under the date</summary>
    public bool HasTags => _tags.Count > 0;

    public IReadOnlySet<string> TagsSet => _tagsSet;

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

    public static int PossibleDuplicateThresholdPercent { get; set; } = PossibleDuplicateThreshold.DefaultPercent;

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
        var kept = relations.Where(r => PossibleDuplicateThreshold.Reaches(r.Score, thresholdPercent)).ToList();
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

    public bool ShowThumbnailImage => Thumbnail is not null;

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
                ? $"{CarManufacturer} {CarModelName} {CarYear} {CarCountry} {CarType} {LiveryName} {Author} {Strings.AuctionBadgeLabel}"
                : $"{CarManufacturer} {CarModelName} {CarYear} {CarCountry} {CarType} {LiveryName} {Author}";
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

    /// <summary>
    /// The author's label in the interface language ("Ann (formerly Old)", "Ann (you)"): it is made when the livery is
    /// read, so a change of language makes it again. True when it changed (author groups are then named again)
    /// </summary>
    public bool RefreshAuthorLabel()
    {
        if (AuthorLabel is not { } label) return false;
        string text = AuthorContext.Format(label);
        if (text == _author) return false;
        Author = text;
        return true;
    }

    public void RefreshLocalizedText()
    {
        RefreshAuthorLabel();
        RefreshCarTexts(); // "as in the interface" follows the interface language
        OnPropertyChanged(nameof(DateDisplay));
        _parseIssueTooltip = null;
        OnPropertyChanged(nameof(ParseIssueTooltip));
        InvalidateDuplicateTexts();
        OnPropertyChanged(nameof(DuplicateMatchTooltip));
        OnPropertyChanged(nameof(PossibleDuplicateBadgeText));
        OnPropertyChanged(nameof(AuthorTooltip));
        OnPropertyChanged(nameof(ThumbnailTooltip));
        OnPropertyChanged(nameof(InstalledBadgeTooltip));
        OnPropertyChanged(nameof(LiveryName));
        OnPropertyChanged(nameof(Author));
        OnPropertyChanged(nameof(AuthorRaw));
        OnPropertyChanged(nameof(AuthorDisplayText));
        OnPropertyChanged(nameof(LockTooltip));
        OnGamePositionTextsChanged();
        RefreshView3D();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>A hint about who a livery is by, worded for the card</summary>
/// <param name="Hint">The hint</param>
/// <param name="TargetName">The author it points to, as shown</param>
/// <param name="Text">"Name: why"</param>
internal sealed record AuthorHintView(AuthorHint Hint, string TargetName, string Text);

