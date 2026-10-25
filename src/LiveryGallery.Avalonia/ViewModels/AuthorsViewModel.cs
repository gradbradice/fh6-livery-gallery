using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LiveryGallery.ViewModels;

/// <param name="Key">Remembered when the user says these are different authors</param>
/// <param name="ExtraTags">Accounts without a card that would join the card</param>
/// <param name="HintKey">Set for a hint (<see cref="AuthorHint.Key"/>): turning it down goes to the hints, not the suggestions</param>
internal sealed record MergeSuggestionViewModel(
    string Key, IReadOnlyList<string> CardIds, IReadOnlyList<string> ExtraTags, string Text, string? HintKey = null);

internal sealed record HiddenAutoCardRowViewModel(string IdentityTag, string NamesText);

internal sealed partial class AuthorsViewModel : ObservableObject
{
    // A name used by more accounts than this is too common to say anything about who is behind them
    private const int MaxAccountsPerSharedName = 5;

    private readonly AuthorCardService _authorCardService;
    private readonly Func<Task<List<LiveryEntry>>> _refreshEntries;
    private readonly HashSet<string> _selectedIds = new(StringComparer.Ordinal);
    private List<LiveryEntry> _allEntries;
    private bool _hasAnyCardsAtAll;

    public ObservableCollection<AuthorCardRowViewModel> Cards { get; } = [];
    public ObservableCollection<MergeSuggestionViewModel> Suggestions { get; } = [];
    public ObservableCollection<HiddenAutoCardRowViewModel> HiddenCards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateText))]
    private string _searchText = "";
    public bool ShowEmptyState => Cards.Count == 0;
    public string EmptyStateText => _hasAnyCardsAtAll ? Strings.AuthorsNoSearchResultsHint : Strings.AuthorsEmptyHint;

    public bool HasSuggestions => Suggestions.Count > 0;
    public bool HasHiddenCards => HiddenCards.Count > 0;
    public string HiddenCardsHeader => string.Format(Strings.AuthorsHiddenHeaderFormat, HiddenCards.Count);

    public bool CanMergeSelected => _selectedIds.Count >= 2;
    public string MergeSelectedText => string.Format(Strings.AuthorsMergeSelectedFormat, _selectedIds.Count);

    public event Action<AuthorCard?>? EditCardRequested;
    public event Action<AuthorCardRowViewModel>? DeleteCardRequested;
    public event Action<AuthorCardRowViewModel>? SplitRequested;

    /// <summary>Card ids, accounts without a card, and how to name them in the question</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>, string>? MergeRequested;

    public AuthorsViewModel(
        AuthorCardService authorCardService, List<LiveryEntry> allEntries, Func<Task<List<LiveryEntry>>> refreshEntries)
    {
        _authorCardService = authorCardService;
        _allEntries = allEntries;
        _refreshEntries = refreshEntries;
        RebuildList();
    }

    partial void OnSearchTextChanged(string value) => RebuildList();

    private void RebuildList()
    {
        // The cards are shared by every ForzaToolkit app: only the authors of this game's liveries are shown here
        var tagsHere = TagsHere();
        // Cards without an account (the user knows the author, not the account yet) and cards with liveries assigned here too
        var assignedHere = _allEntries.Select(e => e.AssignedCardId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var allCards = _authorCardService.GetAll()
            .Where(c => c.IdentityTags.Any(tagsHere.Contains) || c.IdentityTags.Count == 0 || assignedHere.Contains(c.Id))
            .ToList();
        _hasAnyCardsAtAll = allCards.Count > 0;
        _selectedIds.IntersectWith(allCards.Select(c => c.Id));

        var filtered = allCards
            .Where(MatchesSearch)
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var row in Cards) row.PropertyChanged -= OnRowPropertyChanged;
        Cards.Clear();
        foreach (var card in filtered)
        {
            var row = BuildRow(card);
            row.IsSelected = _selectedIds.Contains(card.Id);
            row.PropertyChanged += OnRowPropertyChanged;
            Cards.Add(row);
        }

        RebuildSuggestions(allCards);
        RebuildHiddenCards(tagsHere);

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateText));
        RaiseSelectionChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AuthorCardRowViewModel.IsSelected) || sender is not AuthorCardRowViewModel row) return;
        if (row.IsSelected) _selectedIds.Add(row.Card.Id);
        else _selectedIds.Remove(row.Card.Id);
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(CanMergeSelected));
        OnPropertyChanged(nameof(MergeSelectedText));
    }

    private bool MatchesSearch(AuthorCard card)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (card.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) return true;
        return card.KnownNames.Any(a => a.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    private AuthorCardRowViewModel BuildRow(AuthorCard card)
    {
        var entries = GetEntriesForCard(card);
        return new AuthorCardRowViewModel(card)
        {
            LiveriesCount = entries.Count,
            FavoritesCount = entries.Count(e => e.IsFavorite),
            DuplicatesCount = entries.Count(e => e.IsDuplicate),
            PossibleDuplicatesCount = entries.Count(e => e.IsPossibleDuplicate),
        };
    }

    private List<LiveryEntry> GetEntriesForCard(AuthorCard card)
    {
        var tagSet = card.IdentityTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. _allEntries.Where(e => e.AssignedCardId == card.Id
            || (e.AssignedCardId is null && e.AuthorIdentityTagHex is not null && tagSet.Contains(e.AuthorIdentityTagHex)))];
    }

    /// <summary>The names these accounts used, oldest first (by the latest livery under each name)</summary>
    private List<string> NamesInOrder(IReadOnlySet<string> tags) =>
        [.. _allEntries
            .Where(e => e.AuthorIdentityTagHex is not null && tags.Contains(e.AuthorIdentityTagHex)
                && !e.Data.IsAuthorUnknown && !string.IsNullOrWhiteSpace(e.Data.AuthorRaw))
            .GroupBy(e => e.Data.AuthorRaw.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Max(e => e.DownloadDate ?? DateTime.MinValue))
            .Select(g => g.Key)];

    private string LatestName(string tag) =>
        NamesInOrder(new HashSet<string>([tag], StringComparer.OrdinalIgnoreCase)).LastOrDefault() ?? tag;

    /// <summary>
    /// Cards (and accounts without a card) that share a name are probably one author on several accounts. Only
    /// suggested, never merged on its own: names are freed and taken by other people. Two accounts without any
    /// card are not suggested; picking that shared name in a new card already joins them.
    /// </summary>
    private void RebuildSuggestions(IReadOnlyList<AuthorCard> allCards)
    {
        Suggestions.Clear();
        var dismissed = _authorCardService.GetDismissedSuggestions();
        var hiddenTags = _authorCardService.GetHiddenAutoCards()
            .SelectMany(h => h.IdentityTags).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var namesByTag = _allEntries
            .Where(e => AuthorAccounts.IsAccount(e.AuthorIdentityTagHex) && !e.Data.IsAuthorUnknown && !string.IsNullOrWhiteSpace(e.Data.AuthorRaw))
            .GroupBy(e => e.AuthorIdentityTagHex!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Data.AuthorRaw.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var units = new List<(string Key, string Label, string? CardId, string? Tag, HashSet<string> Names)>();
        foreach (var card in allCards)
        {
            var names = new HashSet<string>(card.KnownNames, StringComparer.OrdinalIgnoreCase);
            foreach (var tag in card.IdentityTags)
                if (namesByTag.TryGetValue(tag, out var observed)) names.UnionWith(observed);
            units.Add(($"c:{card.Id}", card.DisplayName, card.Id, null, names));
        }
        foreach (var (tag, names) in namesByTag)
        {
            if (_authorCardService.FindCardForIdentityTag(tag) is not null || hiddenTags.Contains(tag)) continue;
            units.Add(($"t:{tag}", string.Format(Strings.AuthorsUncardedAccountFormat, LatestName(tag)), null, tag, names));
        }

        var unitsByName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < units.Count; i++)
            foreach (string name in units[i].Names)
            {
                if (PlaceholderTexts.IsUnknownAuthor(name)) continue;
                if (!unitsByName.TryGetValue(name, out var list)) unitsByName[name] = list = [];
                list.Add(i);
            }

        var sharedNameByPair = new Dictionary<(int, int), string>();
        foreach (var (name, indices) in unitsByName)
        {
            if (indices.Count < 2 || indices.Count > MaxAccountsPerSharedName) continue;
            for (int a = 0; a < indices.Count; a++)
                for (int b = a + 1; b < indices.Count; b++)
                {
                    int first = Math.Min(indices[a], indices[b]), second = Math.Max(indices[a], indices[b]);
                    if (units[first].CardId is null && units[second].CardId is null) continue;
                    sharedNameByPair.TryAdd((first, second), name);
                }
        }

        foreach (var ((first, second), name) in sharedNameByPair)
        {
            var one = units[first];
            var two = units[second];
            string key = string.Join("|", new[] { one.Key, two.Key }.Order(StringComparer.Ordinal));
            if (dismissed.Contains(key)) continue;

            Suggestions.Add(new MergeSuggestionViewModel(
                key,
                [.. new[] { one.CardId, two.CardId }.OfType<string>()],
                [.. new[] { one.Tag, two.Tag }.OfType<string>()],
                string.Format(Strings.AuthorsSuggestionFormat, one.Label, two.Label, name)));
        }

        AddArtworkSuggestions(dismissed, hiddenTags);
        AddAccountHints(allCards);
        OnPropertyChanged(nameof(HasSuggestions));
    }

    /// <summary>
    /// Two authors with the same artwork: one person's second account, or a re-upload. Only suggested; the user knows
    /// which, and "different authors" is remembered like for a shared name
    /// </summary>
    private void AddArtworkSuggestions(IReadOnlySet<string> dismissed, IReadOnlySet<string> hiddenTags)
    {
        var inputs = _allEntries.Select(e => AuthorInsights.ToInput(e, _authorCardService)).ToList();
        var names = new AuthorInsights.AuthorNames(_allEntries, _authorCardService);
        var byKey = AuthorInsights.ByKey(_allEntries);
        string accountPrefix = AuthorCard.ImplicitId("");
        foreach (var shared in ArtworkMatches.AcrossAuthors(inputs))
        {
            (string Key, string? CardId, string? Tag) Unit(string authorId) => authorId.StartsWith(accountPrefix, StringComparison.Ordinal)
                ? ($"t:{authorId[accountPrefix.Length..]}", null, authorId[accountPrefix.Length..])
                : ($"c:{authorId}", authorId, null);
            var one = Unit(shared.AuthorIdA);
            var two = Unit(shared.AuthorIdB);
            if ((one.Tag is { } t1 && hiddenTags.Contains(t1)) || (two.Tag is { } t2 && hiddenTags.Contains(t2))) continue;
            string key = string.Join("|", new[] { one.Key, two.Key }.Order(StringComparer.Ordinal));
            if (dismissed.Contains(key) || Suggestions.Any(s => s.Key == key)) continue;

            string LabelOf((string Key, string? CardId, string? Tag) unit) => unit.Tag is { } tag
                ? string.Format(Strings.AuthorsUncardedAccountFormat, names.Of(AuthorCard.ImplicitId(tag)))
                : names.Of(unit.CardId!);
            string artwork = byKey.TryGetValue(shared.KeyA, out var a) ? AuthorInsights.Label(a) : shared.KeyA;
            Suggestions.Add(new MergeSuggestionViewModel(
                key,
                [.. new[] { one.CardId, two.CardId }.OfType<string>()],
                [.. new[] { one.Tag, two.Tag }.OfType<string>()],
                string.Format(Strings.AuthorsArtworkSuggestionFormat, LabelOf(one), LabelOf(two), artwork)));
        }
    }

    /// <summary>Cards without an account: accounts that may be the author's (same name, or the same artwork as an assigned livery)</summary>
    private void AddAccountHints(IReadOnlyList<AuthorCard> allCards)
    {
        var withoutAccount = allCards.Where(c => c.IdentityTags.Count == 0).ToList();
        if (withoutAccount.Count == 0) return;
        var inputs = _allEntries.Select(e => AuthorInsights.ToInput(e, _authorCardService)).ToList();
        var names = new AuthorInsights.AuthorNames(_allEntries, _authorCardService);
        var byKey = AuthorInsights.ByKey(_allEntries);
        string accountPrefix = AuthorCard.ImplicitId("");
        foreach (var (cardId, hints) in AuthorHints.ForCardsWithoutAccounts(inputs, withoutAccount, _authorCardService.GetDismissedHints()))
        {
            foreach (var hint in hints)
            {
                string tag = hint.SubjectId[accountPrefix.Length..];
                Suggestions.Add(new MergeSuggestionViewModel(
                    "hint:" + hint.Key,
                    [cardId],
                    [tag],
                    string.Format(Strings.AuthorsAccountHintFormat,
                        string.Format(Strings.AuthorsUncardedAccountFormat, names.Of(hint.SubjectId)),
                        names.Of(cardId), AuthorInsights.Reason(hint, byKey)),
                    HintKey: hint.Key));
            }
        }
    }

    /// <summary>The accounts of the authors of this game's liveries</summary>
    private HashSet<string> TagsHere() =>
        _allEntries
            .Select(e => e.AuthorIdentityTagHex)
            .Where(AuthorAccounts.IsAccount)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void RebuildHiddenCards(HashSet<string> tagsHere)
    {
        HiddenCards.Clear();
        foreach (var hidden in _authorCardService.GetHiddenAutoCards())
            if (hidden.IdentityTags.Count > 0 && hidden.IdentityTags.Any(tagsHere.Contains))
                HiddenCards.Add(new HiddenAutoCardRowViewModel(hidden.IdentityTags[0], string.Join(", ", hidden.KnownNames)));

        OnPropertyChanged(nameof(HasHiddenCards));
        OnPropertyChanged(nameof(HiddenCardsHeader));
    }

    /// <summary>The accounts the card editor offers (<see cref="AuthorContext.LabelAccounts"/>)</summary>
    public IReadOnlyList<AuthorAccountChoice> AccountChoices(string? excludeCardId) =>
        AuthorContext.LabelAccounts(_authorCardService.BuildAccountIndex(_allEntries.AuthorObservations(), excludeCardId), _allEntries, _authorCardService);

    public async Task<bool> TrySaveCard(AuthorCard card)
    {
        if (!await _authorCardService.TrySave(card)) return false;
        _allEntries = await _refreshEntries();
        RebuildList();
        return true;
    }

    public async Task DeleteCardAsync(string cardId)
    {
        await _authorCardService.Delete(cardId);
        _allEntries = await _refreshEntries();
        RebuildList();
    }

    /// <summary>The joined card, or null when the service refused (see <see cref="AuthorCardService.MergeAsync"/>)</summary>
    public async Task<AuthorCard?> MergeAsync(IReadOnlyList<string> cardIds, IReadOnlyList<string> extraTags)
    {
        var ids = cardIds.ToHashSet(StringComparer.Ordinal);
        var tags = _authorCardService.GetAll()
            .Where(c => ids.Contains(c.Id))
            .SelectMany(c => c.IdentityTags)
            .Concat(extraTags)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var merged = await _authorCardService.MergeAsync(cardIds, extraTags, NamesInOrder(tags));
        if (merged is null) return null;

        _selectedIds.Clear();
        _allEntries = await _refreshEntries();
        RebuildList();
        return _authorCardService.GetAll().FirstOrDefault(c => c.Id == merged.Id) ?? merged;
    }

    /// <summary>The card keeps the account with the most liveries; the others become separate again</summary>
    public async Task<bool> SplitAsync(AuthorCardRowViewModel row)
    {
        var card = row.Card;
        string keepTag = card.IdentityTags
            .OrderByDescending(tag => _allEntries.Count(e => string.Equals(e.AuthorIdentityTagHex, tag, StringComparison.OrdinalIgnoreCase)))
            .First();
        var keepNames = NamesInOrder(new HashSet<string>([keepTag], StringComparer.OrdinalIgnoreCase));

        if (!await _authorCardService.SplitAsync(card.Id, keepTag, keepNames)) return false;
        _allEntries = await _refreshEntries(); // gives the freed accounts their automatic cards back
        RebuildList();
        return true;
    }

    [RelayCommand]
    private void AddCard() => EditCardRequested?.Invoke(null);

    [RelayCommand]
    private void EditCard(AuthorCardRowViewModel row) => EditCardRequested?.Invoke(row.Card);

    [RelayCommand]
    private void DeleteCard(AuthorCardRowViewModel row) => DeleteCardRequested?.Invoke(row);

    [RelayCommand]
    private void SplitCard(AuthorCardRowViewModel row) => SplitRequested?.Invoke(row);

    [RelayCommand]
    private void MergeSelected() => RequestMerge([.. _selectedIds], []);

    [RelayCommand]
    private void MergeSuggestion(MergeSuggestionViewModel suggestion) => RequestMerge(suggestion.CardIds, suggestion.ExtraTags);

    private void RequestMerge(IReadOnlyList<string> cardIds, IReadOnlyList<string> extraTags)
    {
        var cards = _authorCardService.GetAll();
        var names = cardIds
            .Select(id => cards.FirstOrDefault(c => c.Id == id)?.DisplayName)
            .OfType<string>()
            .Concat(extraTags.Select(tag => string.Format(Strings.AuthorsUncardedAccountFormat, LatestName(tag))));
        MergeRequested?.Invoke(cardIds, extraTags, string.Join(", ", names));
    }

    [RelayCommand]
    private async Task DismissSuggestionAsync(MergeSuggestionViewModel suggestion)
    {
        if (suggestion.HintKey is { } hintKey) await _authorCardService.DismissHintAsync(hintKey);
        else await _authorCardService.DismissSuggestionAsync(suggestion.Key);
        RebuildList();
    }

    [RelayCommand]
    private async Task RestoreHiddenAsync(HiddenAutoCardRowViewModel hidden)
    {
        await _authorCardService.UnhideAsync(hidden.IdentityTag);
        _allEntries = await _refreshEntries(); // the next reconcile creates the automatic card again
        RebuildList();
    }
}
