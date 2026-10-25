using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.Liveries.History;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

internal sealed partial class StatisticsViewModel
{
    private const int TopAuthors = 10;
    private const int SmallListSize = 5;
    private const int MinLiveriesForShares = 3;

    private List<AuthorTableRowViewModel> _allAuthorRows = [];
    private AuthorSortColumn _sortColumn = AuthorSortColumn.Liveries;
    private bool _sortDescending = true;

    [ObservableProperty]
    private IReadOnlyList<StatTileViewModel> _authorTiles = [];

    [ObservableProperty]
    private string _topAuthorsTitle = "";

    [ObservableProperty]
    private IReadOnlyList<ShareRowViewModel> _topAuthorRows = [];

    [ObservableProperty]
    private IReadOnlyList<BreakdownCardViewModel> _authorCards = [];

    [ObservableProperty]
    private string _authorSearch = "";

    [ObservableProperty]
    private bool _noAuthorsFound;

    public ObservableCollection<AuthorTableRowViewModel> AuthorRows { get; } = [];

    partial void OnAuthorSearchChanged(string value) => ApplyAuthorRows();

    [RelayCommand]
    private void SortAuthors(string column)
    {
        var parsed = Enum.Parse<AuthorSortColumn>(column);
        _sortDescending = parsed == _sortColumn ? !_sortDescending : parsed != AuthorSortColumn.Name;
        _sortColumn = parsed;
        ApplyAuthorRows();
    }

    private IReadOnlyList<AuthorStatisticsRow> AuthorRowsFor(IReadOnlyList<LiveryEntry> items) =>
        AuthorStatsFor(items).Rows;

    private AuthorStatistics AuthorStatsFor(IReadOnlyList<LiveryEntry> items)
    {
        var firstSeen = _events.Count == 0 ? null
            : LiveryHistoryQueries.FirstSeen(_events.Where(e => _timelineOptions.Include?.Invoke(e) ?? true), _host.Source.AuthorKey);
        return AuthorStatisticsCalculator.Calculate(items, _host.Source.AuthorKey, new AuthorStatisticsOptions
        {
            TopCount = TopAuthors,
            NewSinceUtc = DateTime.UtcNow - RecentPeriod,
            FirstSeenUtc = firstSeen,
        });
    }

    private void BuildAuthors()
    {
        var stats = AuthorStatsFor(_items);
        AuthorTiles =
        [
            new(N(stats.Authors), Strings.StatsAuthorsTileUnique, string.Format(Strings.StatsTileWithoutAuthorFormat, N(stats.WithoutAuthor)), null, null),
            new(Percent(stats.TopShare), string.Format(Strings.StatsAuthorsTileTopFormat, stats.TopCount), Strings.StatsAuthorsTopDetail,
                string.Format(Strings.StatsAuthorsTopTooltipFormat, stats.TopCount, N((int)Math.Round(stats.TopShare * stats.LiveriesWithAuthor)), N(stats.LiveriesWithAuthor)), null),
            new(N(stats.SingleLiveryAuthors), Strings.StatsAuthorsTileSingle,
                string.Format(Strings.StatsAuthorsSingleDetailFormat, Percent(stats.Authors == 0 ? 0 : (double)stats.SingleLiveryAuthors / stats.Authors)), null, null),
            new(N(stats.NewAuthors), Strings.StatsAuthorsTileNew, Strings.StatsAuthorsNewDetail, null, null),
        ];

        TopAuthorsTitle = string.Format(Strings.StatsTopAuthorsTitleFormat, stats.TopCount);
        TopAuthorRows = Rows(stats.Rows.Take(TopAuthors), r => r.Name, r => r.Liveries, stats.LiveriesWithAuthor,
            r => OpenAuthorCommand(r.SampleKey), Strings.StatsAuthorRowTooltip);

        var withEnough = stats.Rows.Where(r => r.Liveries >= MinLiveriesForShares).ToList();
        var cards = new List<BreakdownCardViewModel>
        {
            SharedCard(),
            new(Strings.StatsGeneratorsTitle,
                Rows(withEnough.Where(r => r.Generated > 0).OrderByDescending(r => r.GeneratedShare).ThenByDescending(r => r.Generated).Take(SmallListSize),
                    r => r.Name, r => r.Generated, 0, r => OpenAuthorCommand(r.SampleKey), Strings.StatsAuthorRowTooltip,
                    r => $"{N(r.Generated)} · {Percent(r.GeneratedShare)}"),
                null, Strings.StatsGeneratorsNone),
            new(Strings.StatsFavoriteAuthorsTitle,
                Rows(withEnough.Where(r => r.Favorites > 0).OrderByDescending(r => r.FavoriteShare).ThenByDescending(r => r.Favorites).Take(SmallListSize),
                    r => r.Name, r => r.Favorites, 0, r => OpenAuthorCommand(r.SampleKey), Strings.StatsAuthorRowTooltip,
                    r => $"{N(r.Favorites)} · {Percent(r.FavoriteShare)}"),
                null, Strings.StatsNothing),
            DeparturesCard(),
        };
        AuthorCards = cards;

        _allAuthorRows = [.. stats.Rows.Select(row =>
        {
            var sample = _byKey.GetValueOrDefault(row.SampleKey);
            return new AuthorTableRowViewModel(row, sample, new RelayCommand(() => { if (sample is not null) _host.OpenAuthor(sample); }));
        })];
        ApplyAuthorRows();
    }

    private RelayCommand? OpenAuthorCommand(string sampleKey) =>
        _byKey.TryGetValue(sampleKey, out var sample) ? new RelayCommand(() => _host.OpenAuthor(sample)) : null;

    /// <summary>Two authors with much the same artwork: one person's second account, or a re-upload</summary>
    private BreakdownCardViewModel SharedCard()
    {
        var shared = _host.Source.SharedArtworks(_items).Take(SmallListSize).ToList();
        var rows = shared.Select(pair =>
        {
            string a = _byKey.GetValueOrDefault(pair.KeyA)?.Author ?? pair.AuthorIdA;
            string b = _byKey.GetValueOrDefault(pair.KeyB)?.Author ?? pair.AuthorIdB;
            string label = $"{a} · {b}";
            var command = Show([pair.KeyA, pair.KeyB], label);
            return new ShareRowViewModel(label, Percent(pair.Similarity), "", pair.Similarity, command, Strings.StatsSharedRowTooltip);
        }).ToList();
        return new BreakdownCardViewModel(Strings.StatsSharedTitle, rows, null, Strings.StatsSharedNone);
    }

    private BreakdownCardViewModel DeparturesCard()
    {
        if (_events.Count == 0) return new BreakdownCardViewModel(Strings.StatsDeparturesTitle, [], null, Strings.StatsNoJournalShort);
        var gone = LiveryTimelineCalculator.DeparturesByAuthor(_events, _timelineOptions).Take(SmallListSize).ToList();
        var rows = Rows(gone, g => g.Name ?? Strings.StatsUnknownValue, g => g.Deleted + g.Archived, 0,
            countText: g => g.Archived == 0 ? N(g.Deleted) : string.Format(Strings.StatsDeparturesCellFormat, N(g.Deleted), N(g.Archived)));
        return new BreakdownCardViewModel(Strings.StatsDeparturesTitle, rows, null, Strings.StatsNothing);
    }

    private void ApplyAuthorRows()
    {
        IEnumerable<AuthorTableRowViewModel> rows = _allAuthorRows;
        if (!string.IsNullOrWhiteSpace(AuthorSearch))
            rows = rows.Where(r => r.Name.Contains(AuthorSearch.Trim(), StringComparison.CurrentCultureIgnoreCase));
        rows = _sortColumn switch
        {
            AuthorSortColumn.Name => Order(rows, r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            AuthorSortColumn.Cars => Order(rows, r => r.Row.Cars),
            AuthorSortColumn.Favorites => Order(rows, r => r.Row.FavoriteShare),
            AuthorSortColumn.FirstDownload => Order(rows, r => r.Row.FirstDownload ?? DateTime.MinValue),
            AuthorSortColumn.LastDownload => Order(rows, r => r.Row.LastDownload ?? DateTime.MinValue),
            _ => Order(rows, r => r.Row.Liveries),
        };
        AuthorRows.Clear();
        foreach (var row in rows) AuthorRows.Add(row);
        NoAuthorsFound = AuthorRows.Count == 0;
    }

    private IEnumerable<AuthorTableRowViewModel> Order<TKey>(
        IEnumerable<AuthorTableRowViewModel> rows, Func<AuthorTableRowViewModel, TKey> key, IComparer<TKey>? comparer = null)
    {
        var ordered = _sortDescending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
        return ordered.ThenByDescending(r => r.Row.Liveries).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private enum AuthorSortColumn
    {
        Name,
        Liveries,
        Cars,
        Favorites,
        FirstDownload,
        LastDownload,
    }
}
