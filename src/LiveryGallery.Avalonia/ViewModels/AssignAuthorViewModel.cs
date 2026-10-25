using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

/// <summary>An author a livery can be assigned to, as the list shows it</summary>
/// <param name="Card">The card (one made on the fly for an account without a card)</param>
/// <param name="Name">How it is named</param>
/// <param name="Detail">Its nicknames, or why it is hinted</param>
/// <param name="IsHint">It is one of the hints: shown first</param>
internal sealed record AuthorChoiceRow(AuthorCard Card, string Name, string Detail, bool IsHint)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// "Whose livery is it?": the hints first, then every author, with a search; saving needs the user to say they are sure,
/// because the livery's own id cannot back the choice up. Authors without any name (an account whose liveries never
/// gave one) are left out: nothing tells the user who they are. The livery's current card is kept even then
/// </summary>
internal sealed partial class AssignAuthorViewModel : ObservableObject
{
    private readonly List<AuthorChoiceRow> _allRows = [];

    public string LiveryText { get; }
    public string NoteText { get; }

    /// <summary>Selected liveries left out (their id is an account's), when there are any</summary>
    public string SkippedText { get; }
    public bool HasSkipped => SkippedText.Length > 0;

    /// <summary>"Whose liveries are these?" for several</summary>
    public string TitleText { get; }

    /// <summary>How many liveries get the author</summary>
    public int LiveryCount { get; }

    public ObservableCollection<AuthorChoiceRow> Rows { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private AuthorChoiceRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isConfirmed;

    public bool CanSave => SelectedRow is not null && IsConfirmed;
    public bool ShowNoResults => Rows.Count == 0;

    public event Action<AuthorCard?>? CloseRequested;
    public event Action? NewCardRequested;

    public AssignAuthorViewModel(LiveryEntry entry, IReadOnlyList<AuthorCard> choices, IReadOnlyList<AuthorHintView> hints,
        Func<string, AuthorCard?> cardOfTarget)
        : this([entry], choices, hints, cardOfTarget)
    {
    }

    /// <param name="entries">The liveries that get one author (at least one)</param>
    /// <param name="choices">Every author they can be assigned to</param>
    /// <param name="hints">Who they may be by: the hints of every livery, the first of each author kept</param>
    /// <param name="cardOfTarget">The card a hint points to</param>
    /// <param name="skipped">Selected liveries left out because their id is an account's</param>
    public AssignAuthorViewModel(IReadOnlyList<LiveryEntry> entries, IReadOnlyList<AuthorCard> choices,
        IReadOnlyList<AuthorHintView> hints, Func<string, AuthorCard?> cardOfTarget, int skipped = 0)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entries.Count);
        LiveryCount = entries.Count;
        if (entries.Count == 1)
        {
            var entry = entries[0];
            string car = $"{entry.CarManufacturer} {entry.CarModelName}".Trim();
            LiveryText = string.IsNullOrWhiteSpace(entry.Data.LiveryName) ? car : $"{entry.Data.LiveryName} — {car}";
            NoteText = entry.Data.AuthorIdKind == AuthorIdKind.Invalid ? Strings.AssignAuthorInvalidIdNote : Strings.AssignAuthorNoIdNote;
            TitleText = Strings.AssignAuthorDialogTitle;
        }
        else
        {
            LiveryText = string.Format(Strings.AssignAuthorLiveriesFormat, entries.Count);
            NoteText = Strings.AssignAuthorSeveralNote;
            TitleText = Strings.AssignAuthorDialogTitleSeveral;
        }
        SkippedText = skipped > 0 ? string.Format(Strings.AssignAuthorSkippedFormat, skipped) : "";

        // The author they all have now is picked; a card one of them has stays listed even without a name
        var current = entries.Select(e => e.AssignedCardId).Distinct().ToList();
        string? common = current.Count == 1 ? current[0] : null;
        var assigned = current.OfType<string>().ToHashSet(StringComparer.Ordinal);

        var hinted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hint in hints)
        {
            if (cardOfTarget(hint.Hint.TargetId) is not { } card || !CanBeShown(card) || !hinted.Add(card.Id)) continue;
            _allRows.Add(new AuthorChoiceRow(card, hint.TargetName, AuthorInsightsReason(hint), IsHint: true));
        }
        foreach (var card in choices)
        {
            if (hinted.Contains(card.Id) || !CanBeShown(card)) continue;
            _allRows.Add(new AuthorChoiceRow(card, card.DisplayName, Detail(card), IsHint: false));
        }
        ApplySearch();
        if (common is not null)
            SelectedRow = Rows.FirstOrDefault(r => r.IsHint && r.Card.Id == common) ?? Rows.FirstOrDefault(r => r.Card.Id == common);

        // A card the user can recognise: it has a name of its own or one from its liveries
        bool CanBeShown(AuthorCard card) => assigned.Contains(card.Id) || HasName(card);
    }

    // "Name: why" without the name: the row shows the name above
    private static string AuthorInsightsReason(AuthorHintView hint) =>
        hint.Text.StartsWith(hint.TargetName + ": ", StringComparison.Ordinal) ? hint.Text[(hint.TargetName.Length + 2)..] : hint.Text;

    internal static bool HasName(AuthorCard card) =>
        !string.IsNullOrWhiteSpace(card.CustomName) || card.KnownNames.Any(n => !string.IsNullOrWhiteSpace(n));

    private static string Detail(AuthorCard card) =>
        card.IdentityTags.Count == 0 ? "" : string.Join(", ", card.KnownNames.Where(n => n != card.DisplayName));

    partial void OnSearchTextChanged(string value) => ApplySearch();

    private void ApplySearch()
    {
        var keep = SelectedRow;
        Rows.Clear();
        foreach (var row in _allRows)
        {
            if (!string.IsNullOrWhiteSpace(SearchText)
                && !row.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
                && !row.Card.KnownNames.Any(n => n.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)))
                continue;
            Rows.Add(row);
        }
        SelectedRow = keep is not null && Rows.Contains(keep) ? keep : null;
        OnPropertyChanged(nameof(ShowNoResults));
    }

    /// <summary>A card the user just made: listed and picked</summary>
    public void AddAndSelect(AuthorCard card)
    {
        var row = new AuthorChoiceRow(card, card.DisplayName, Detail(card), IsHint: false);
        _allRows.Insert(_allRows.Count(r => r.IsHint), row);
        SearchText = "";
        ApplySearch();
        SelectedRow = row;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => CloseRequested?.Invoke(SelectedRow?.Card);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    [RelayCommand]
    private void NewCard() => NewCardRequested?.Invoke();
}
