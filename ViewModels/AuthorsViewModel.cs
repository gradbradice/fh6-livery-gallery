using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class AuthorsViewModel : ObservableObject
{
    private readonly AuthorCardService _authorCardService;
    private readonly Func<Task<List<LiveryEntry>>> _refreshEntries;
    private List<LiveryEntry> _allEntries;
    private bool _hasAnyCardsAtAll;

    public ObservableCollection<AuthorCardRowViewModel> Cards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateText))]
    private string _searchText = "";
    public bool ShowEmptyState => Cards.Count == 0;
    public string EmptyStateText => _hasAnyCardsAtAll ? Strings.AuthorsNoSearchResultsHint : Strings.AuthorsEmptyHint;
    public event Action<AuthorCard?>? EditCardRequested;
    public event Action<AuthorCardRowViewModel>? DeleteCardRequested;

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
        var allCards = _authorCardService.GetAll();
        _hasAnyCardsAtAll = allCards.Count > 0;

        var filtered = allCards
            .Where(MatchesSearch)
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

        Cards.Clear();
        foreach (var card in filtered) Cards.Add(BuildRow(card));

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateText));
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
        return [.. _allEntries.Where(e => e.AuthorIdentityTagHex is not null && tagSet.Contains(e.AuthorIdentityTagHex))];
    }

    public (List<string> AvailableAliases, Dictionary<string, List<string>> NameToTags) BuildAliasIndex(string? excludeCardId) =>
        _authorCardService.BuildAliasIndex(_allEntries, excludeCardId);

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

    [RelayCommand]
    private void AddCard() => EditCardRequested?.Invoke(null);

    [RelayCommand]
    private void EditCard(AuthorCardRowViewModel row) => EditCardRequested?.Invoke(row.Card);

    [RelayCommand]
    private void DeleteCard(AuthorCardRowViewModel row) => DeleteCardRequested?.Invoke(row);
}
