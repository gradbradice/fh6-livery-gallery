using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.Liveries.History;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>Where a tile or a row of the statistics leads in the gallery</summary>
internal enum StatisticsTarget
{
    Everything,
    ByAuthor,
    ByManufacturer,
    Favorites,
    Installed,
    Auction,
    Mine,
    Generated,
    Duplicates,
    PaintOnly,
}

/// <summary>The pages of the statistics window, as the list on the left names them</summary>
internal enum StatisticsPage
{
    Overview,
    Health,
    Collection,
    Cars,
    Time,
    Authors,
    Liveries,
}

/// <summary>How many months the charts show</summary>
internal enum HistoryRange
{
    Year,
    TwoYears,
    All,
}

/// <summary>Another account's collection at a glance, for the overview</summary>
/// <param name="Title">How the account is named</param>
/// <param name="IsCurrent">The account the gallery shows</param>
/// <param name="Liveries">Its liveries the save's limit counts</param>
/// <param name="Authors">Distinct author accounts</param>
/// <param name="Newest">Its newest livery's download date</param>
internal sealed record AccountSummary(string Title, bool IsCurrent, int Liveries, int Authors, DateTime? Newest);

/// <summary>What the statistics window reads from the gallery and what it asks the gallery to do</summary>
/// <param name="All">Every livery of the account</param>
/// <param name="Shown">What the gallery's filters show now</param>
/// <param name="History">The account's journal</param>
/// <param name="Source">Who a livery or a journal event is by</param>
/// <param name="IsInstalledKnown">The livery server's list is there (the "on cars" numbers mean something)</param>
/// <param name="Navigate">Sets the gallery's filter for a tile</param>
/// <param name="ApplyQuickFilter">Narrows the gallery to a row or a set of liveries</param>
/// <param name="SelectTag">Narrows the gallery to a tag</param>
/// <param name="OpenAuthor">Opens the author window of a livery's author</param>
internal sealed record StatisticsHost(
    Func<IReadOnlyList<LiveryEntry>> All,
    Func<IReadOnlyList<LiveryEntry>> Shown,
    Func<IReadOnlyList<LiveryHistoryEvent>> History,
    StatisticsSource Source,
    Func<bool> IsInstalledKnown,
    Action<StatisticsTarget> Navigate,
    Action<QuickFilter> ApplyQuickFilter,
    Action<string> SelectTag,
    Action<LiveryEntry> OpenAuthor)
{
    /// <summary>How many liveries the save may hold (<see cref="Profiles.GameProfile.MaxLiveries"/>)</summary>
    public int? MaxLiveries { get; init; }

    /// <summary>The backups' manifests (read from the files: called seldom); null when there are no backups to know</summary>
    public Func<IReadOnlyList<LiveryBackupManifest?>>? Backups { get; init; }

    /// <summary>The account's liveries in the archive</summary>
    public Func<IReadOnlyList<ArchivedLiveryEntry>>? Archive { get; init; }

    /// <summary>Every car of the game: id and manufacturer as the car database names it</summary>
    public Func<IReadOnlyList<(int CarId, string Manufacturer)>>? GameCars { get; init; }

    /// <summary>A manufacturer as the gallery shows it, from the car database's text</summary>
    public Func<string, string?>? ManufacturerLabel { get; init; }

    /// <summary>Every account's collection at a glance (does disk work: called once per window)</summary>
    public Func<Task<IReadOnlyList<AccountSummary>>>? Accounts { get; init; }
}

/// <summary>One number in a tile</summary>
internal sealed record StatTileViewModel(string Value, string Label, string Detail, string? Tooltip, ICommand? Command)
{
    public bool IsClickable => Command is not null;
}

/// <summary>A row of a breakdown: label, bar, count and share</summary>
internal sealed record ShareRowViewModel(string Label, string CountText, string PercentText, double Fraction, ICommand? Command, string? Tooltip,
    string? DetailText = null)
{
    public bool IsClickable => Command is not null;
}

/// <summary>A card of rows</summary>
internal sealed record BreakdownCardViewModel(string Title, IReadOnlyList<ShareRowViewModel> Rows, string? MoreText, string? EmptyText)
{
    public bool HasMore => MoreText is not null;
    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>A line of a health card or a record: what it is, its value, and where it leads</summary>
internal sealed record FactViewModel(string Label, string Value, ICommand? Command, string? Tooltip = null)
{
    public bool IsClickable => Command is not null;
}

/// <summary>A row of the authors' table</summary>
internal sealed record AuthorTableRowViewModel(AuthorStatisticsRow Row, LiveryEntry? Sample, ICommand Open)
{
    private static CultureInfo Culture => AppLocalisationService.Culture;
    public string Name => Row.Name;
    public string LiveriesText => Row.Liveries.ToString("N0", Culture);
    public string CarsText => Row.Cars.ToString("N0", Culture);
    public string FavoritesText => Row.Favorites == 0 ? "—" : $"{Row.Favorites.ToString("N0", Culture)} · {StatisticsViewModel.Percent(Row.FavoriteShare)}";
    // The first and the latest download of his liveries in the collection (the download dates, not the journal)
    public string FirstDownloadText => Row.FirstDownload is { } first ? first.ToString("d", Culture) : "—";
    public string LastDownloadText => Row.LastDownload is { } last ? last.ToString("d", Culture) : "—";
}

/// <summary>The legend of a chart: a short stroke of the series colour and its name</summary>
internal sealed record ChartLegendItem(string Series, string Label);

/// <summary>
/// The statistics window, page by page (overview, health, collection, cars, time, authors, liveries). Everything is
/// computed by ForzaToolkit.Liveries <c>Statistics</c> over the whole gallery or what its filters show; this words and
/// arranges it. Only the open page is computed, again when the scope, a filter or the liveries change
/// </summary>
internal sealed partial class StatisticsViewModel : ObservableObject
{
    private const int RowsPerCard = 8;
    private static readonly TimeSpan RecentPeriod = TimeSpan.FromDays(30);

    private readonly StatisticsHost _host;
    private IReadOnlyList<LiveryEntry> _all = [];
    private IReadOnlyList<LiveryEntry> _items = [];
    private Dictionary<string, LiveryEntry> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LiveryHistoryEvent> _events = [];
    private LiveryTimelineOptions _timelineOptions = new();
    private readonly HashSet<StatisticsPage> _built = [];

    public StatisticsViewModel(StatisticsHost host)
    {
        _host = host;
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllScope))]
    private bool _isShownScope;

    public bool IsAllScope => !IsShownScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewPage), nameof(IsHealthPage), nameof(IsCollectionPage), nameof(IsCarsPage),
        nameof(IsTimePage), nameof(IsAuthorsPage), nameof(IsLiveriesPage), nameof(PageTitle))]
    private StatisticsPage _page;

    public bool IsOverviewPage => Page == StatisticsPage.Overview;
    public bool IsHealthPage => Page == StatisticsPage.Health;
    public bool IsCollectionPage => Page == StatisticsPage.Collection;
    public bool IsCarsPage => Page == StatisticsPage.Cars;
    public bool IsTimePage => Page == StatisticsPage.Time;
    public bool IsAuthorsPage => Page == StatisticsPage.Authors;
    public bool IsLiveriesPage => Page == StatisticsPage.Liveries;

    /// <summary>A page's name, as the list on the left and the page's title show it</summary>
    public static string TitleOf(StatisticsPage page) => page switch
    {
        StatisticsPage.Health => Strings.StatsPageHealth,
        StatisticsPage.Collection => Strings.StatsTabCollection,
        StatisticsPage.Cars => Strings.StatsPageCars,
        StatisticsPage.Time => Strings.StatsTabTime,
        StatisticsPage.Authors => Strings.StatsTabAuthors,
        StatisticsPage.Liveries => Strings.StatsPageLiveries,
        _ => Strings.StatsPageOverview,
    };

    public string PageTitle => TitleOf(Page);

    [ObservableProperty]
    private string _scopeShownText = "";

    [ObservableProperty]
    private string? _scopeNote;

    partial void OnIsShownScopeChanged(bool value) => Refresh();

    partial void OnPageChanged(StatisticsPage value) => BuildPage(value);

    [RelayCommand]
    private void ShowAll() => IsShownScope = false;

    [RelayCommand]
    private void ShowShown() => IsShownScope = true;

    [RelayCommand]
    private void OpenPage(StatisticsPage page) => Page = page;

    /// <summary>Reads the gallery and the journal again (the scope, a filter or the liveries changed); the open page is computed again</summary>
    public void Refresh()
    {
        _all = _host.All();
        var shown = _host.Shown();
        _items = IsShownScope ? shown : _all;
        _byKey = AuthorInsights.ByKey(_items);
        var culture = AppLocalisationService.Culture;
        ScopeShownText = string.Format(Strings.StatsScopeShownFormat, shown.Count.ToString("N0", culture));
        ScopeNote = IsShownScope && shown.Count != _all.Count
            ? string.Format(Strings.StatsScopeNoteFormat, shown.Count.ToString("N0", culture), _all.Count.ToString("N0", culture))
            : null;
        _events = _host.History();
        _timelineOptions = TimelineOptions();
        _built.Clear();
        BuildPage(Page);
    }

    /// <summary>Computes a page the first time it is opened after a refresh</summary>
    private void BuildPage(StatisticsPage page)
    {
        if (!_built.Add(page)) return;
        switch (page)
        {
            case StatisticsPage.Overview: BuildOverview(); break;
            case StatisticsPage.Health: BuildHealth(); break;
            case StatisticsPage.Collection: BuildCollection(); break;
            case StatisticsPage.Cars: BuildCars(); break;
            case StatisticsPage.Time: BuildTime(); break;
            case StatisticsPage.Authors: BuildAuthors(); break;
            case StatisticsPage.Liveries: BuildLiveries(); break;
        }
    }

    private LiveryTimelineOptions TimelineOptions()
    {
        Func<LiveryHistoryEvent, bool>? include = null;
        if (IsShownScope)
        {
            var hashes = _items.Select(e => e.CLiveryHash).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keys = _items.Select(e => e.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            include = e => (e.Hash is { } hash && hashes.Contains(hash)) || (e.Key is { } key && keys.Contains(key));
        }
        return new LiveryTimelineOptions { Include = include, AuthorOf = _host.Source.AuthorKey };
    }

    // ---- Collection ----

    [ObservableProperty]
    private IReadOnlyList<BreakdownCardViewModel> _breakdowns = [];

    private void BuildCollection()
    {
        Breakdowns =
        [
            Card(Strings.StatsSectionManufacturers, LiveryDimension.Manufacturer, QuickFilter.ForManufacturer),
            Card(Strings.StatsSectionCountries, LiveryDimension.Country, QuickFilter.ForCountry),
            Card(Strings.StatsSectionCarTypes, LiveryDimension.CarType, QuickFilter.ForCarType),
            Card(Strings.StatsSectionDecades, LiveryDimension.CarDecade, null),
            TagCard(),
            TraitsCard(),
        ];
    }

    private BreakdownCardViewModel Card(string title, LiveryDimension dimension, Func<IGalleryItem, QuickFilter>? filter, int rowsShown = RowsPerCard)
    {
        var breakdown = LiveryStatistics.Breakdown(_items, dimension);
        int max = breakdown.Rows.Count == 0 ? 1 : breakdown.Rows.Max(r => r.Count);
        var rows = breakdown.Rows.Take(rowsShown).Select(row =>
        {
            string label = dimension == LiveryDimension.CarDecade
                ? row.Key.Length == 0 ? Strings.StatsUnknownValue : string.Format(Strings.StatsDecadeFormat, row.Key)
                : string.IsNullOrWhiteSpace(row.Label) ? Strings.StatsUnknownValue : row.Label;
            ICommand? command = filter is not null && _byKey.TryGetValue(row.SampleKey, out var sample)
                ? new RelayCommand(() => _host.ApplyQuickFilter(filter(sample)))
                : null;
            return new ShareRowViewModel(label, N(row.Count), Percent((double)row.Count / Math.Max(1, breakdown.Total)),
                (double)row.Count / max, command, command is null ? null : Strings.StatsRowTooltip);
        }).ToList();
        int more = breakdown.Rows.Count - rows.Count;
        return new BreakdownCardViewModel(title, rows, more > 0 ? string.Format(Strings.StatsMoreFormat, N(more)) : null,
            rows.Count == 0 ? Strings.StatsNothing : null);
    }

    private BreakdownCardViewModel TagCard()
    {
        var breakdown = LiveryStatistics.Breakdown(_items, LiveryDimension.Tag);
        int max = breakdown.Rows.Count == 0 ? 1 : breakdown.Rows.Max(r => r.Count);
        var rows = breakdown.Rows.Take(RowsPerCard).Select(row =>
        {
            string tag = row.Key;
            return new ShareRowViewModel(row.Label, N(row.Count), Percent((double)row.Count / Math.Max(1, _items.Count)),
                (double)row.Count / max, new RelayCommand(() => _host.SelectTag(tag)), Strings.StatsRowTooltip);
        }).ToList();
        int more = breakdown.Rows.Count - rows.Count;
        int untagged = _items.Count(i => i.TagsSet.Count == 0);
        string? note = rows.Count == 0 ? Strings.StatsNoTags : null;
        var card = new BreakdownCardViewModel(Strings.StatsSectionTags, rows,
            more > 0 ? string.Format(Strings.StatsMoreFormat, N(more)) : untagged > 0 && rows.Count > 0 ? string.Format(Strings.StatsUntaggedFormat, N(untagged)) : null,
            note);
        return card;
    }

    private BreakdownCardViewModel TraitsCard()
    {
        var o = LiveryStatistics.Overview(_items, _host.Source.AuthorKey);
        var traits = new (string Label, int Count, StatisticsTarget Target)[]
        {
            (Strings.StatsTraitMine, o.Mine, StatisticsTarget.Mine),
            (Strings.StatsTraitGenerated, o.Generated, StatisticsTarget.Generated),
            (Strings.StatsTraitDuplicates, o.Duplicates + o.PossibleDuplicates, StatisticsTarget.Duplicates),
            (Strings.StatsTraitPaintOnly, _items.Count(i => i.HasNoLayers), StatisticsTarget.PaintOnly),
        };
        int max = Math.Max(1, traits.Max(t => t.Count));
        var rows = traits.Select(t => new ShareRowViewModel(t.Label, N(t.Count), Percent((double)t.Count / Math.Max(1, o.Liveries)),
            (double)t.Count / max, t.Count > 0 ? Go(t.Target) : null, t.Count > 0 ? Strings.StatsRowTooltip : null)).ToList();
        return new BreakdownCardViewModel(Strings.StatsSectionTraits, rows, null, null);
    }

    // ---- Export ----

    /// <summary>The liveries counted now (the scope), one row each</summary>
    public void ExportLiveries(TextWriter writer) => LiveryCsv.WriteLiveries(writer, _items);

    /// <summary>The authors of the liveries counted now, one row each</summary>
    public void ExportAuthors(TextWriter writer) => LiveryCsv.WriteAuthors(writer, AuthorRowsFor(_items));

    // ---- Helpers ----

    private RelayCommand Go(StatisticsTarget target) => new(() => _host.Navigate(target));

    /// <summary>Shows exactly these liveries in the gallery</summary>
    private RelayCommand? Show(IReadOnlyCollection<string>? keys, string name) =>
        keys is { Count: > 0 } ? new RelayCommand(() => _host.ApplyQuickFilter(QuickFilter.ForKeys(keys, name))) : null;

    private static string N(int value) => value.ToString("N0", AppLocalisationService.Culture);

    /// <summary>"120 liveries" with the plural form of the app's language</summary>
    internal static string LiveryCount(int count)
    {
        string format = PluralForm(count, AppLocalisationService.Culture) switch
        {
            Plural.One => Strings.StatsLiveryCountOne,
            Plural.Few => Strings.StatsLiveryCountFew,
            _ => Strings.StatsLiveryCountMany,
        };
        return string.Format(AppLocalisationService.Culture, format, N(count));
    }

    /// <summary>
    /// Which plural form a count takes. Russian has three (1 ливрея, 2 ливреи, 5 ливрей); French counts 0 with 1; the
    /// other languages of the app have one form for 1 and one for the rest (Japanese, Korean and Chinese give all three
    /// the same text)
    /// </summary>
    internal static Plural PluralForm(int count, CultureInfo culture)
    {
        int n = Math.Abs(count);
        switch (culture.TwoLetterISOLanguageName)
        {
            case "ru":
                if (n % 10 == 1 && n % 100 != 11) return Plural.One;
                return n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? Plural.Few : Plural.Many;
            case "fr":
                return n <= 1 ? Plural.One : Plural.Many;
            default:
                return n == 1 ? Plural.One : Plural.Many;
        }
    }

    internal enum Plural { One, Few, Many }

    private static string Share(int part, int whole) =>
        string.Format(Strings.StatsTileShareFormat, Percent(whole == 0 ? 0 : (double)part / whole));

    private static string Date(DateTime? date) => date is { } d ? d.ToString("d", AppLocalisationService.Culture) : "—";

    /// <summary>A share as a whole percent; under 1% shows "&lt;1%", not 0%</summary>
    public static string Percent(double share)
    {
        if (share <= 0) return 0.ToString("P0", AppLocalisationService.Culture);
        if (share < 0.01) return "<" + 0.01.ToString("P0", AppLocalisationService.Culture);
        return share.ToString("P0", AppLocalisationService.Culture);
    }

    private static List<ShareRowViewModel> Rows<T>(IEnumerable<T> source, Func<T, string> label, Func<T, int> count, int whole,
        Func<T, ICommand?>? command = null, string? tooltip = null, Func<T, string>? countText = null)
    {
        var list = source.ToList();
        int max = list.Count == 0 ? 1 : Math.Max(1, list.Max(count));
        return [.. list.Select(item =>
        {
            var cmd = command?.Invoke(item);
            return new ShareRowViewModel(label(item), countText?.Invoke(item) ?? N(count(item)),
                whole == 0 ? "" : Percent((double)count(item) / whole), (double)count(item) / max, cmd, cmd is null ? null : tooltip);
        })];
    }
}
