using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

internal sealed partial class StatisticsViewModel
{
    private const int MakesShown = 12;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCoverageByLiveries), nameof(IsCoverageByCars), nameof(IsCoverageByShare))]
    private CoverageOrder _coverageOrder = CoverageOrder.Liveries;

    public bool IsCoverageByLiveries => CoverageOrder == CoverageOrder.Liveries;
    public bool IsCoverageByCars => CoverageOrder == CoverageOrder.Cars;
    public bool IsCoverageByShare => CoverageOrder == CoverageOrder.Share;

    partial void OnCoverageOrderChanged(CoverageOrder value) => BuildCars();

    [RelayCommand]
    private void SelectCoverageOrder(CoverageOrder order) => CoverageOrder = order;

    [ObservableProperty]
    private IReadOnlyList<StatTileViewModel> _carTiles = [];

    [ObservableProperty]
    private BreakdownCardViewModel? _coverageCard;

    [ObservableProperty]
    private BreakdownCardViewModel? _topCarsCard;

    [ObservableProperty]
    private bool _hasGameCars;

    private void BuildCars()
    {
        TopCarsCard = Card(Strings.StatsTopCarsTitle, LiveryDimension.Car, QuickFilter.ForCar, 10);
        var gameCars = _host.GameCars?.Invoke() ?? [];
        HasGameCars = gameCars.Count > 0;
        if (!HasGameCars)
        {
            CarTiles = [];
            CoverageCard = new BreakdownCardViewModel(Strings.StatsCoverageTitle, [], null, Strings.StatsNoCarDatabase);
            return;
        }

        var coverage = CarCoverageCalculator.Calculate(_items, gameCars, _host.ManufacturerLabel);
        CarTiles =
        [
            new(N(coverage.GameCars), Strings.StatsCarsInGame, string.Format(Strings.StatsTileManufacturersFormat, N(coverage.Manufacturers.Count)), null, null),
            new(N(coverage.Covered), Strings.StatsCarsCovered, string.Format(Strings.StatsTileShareFormat, Percent(coverage.Share)), null, null),
            new(N(coverage.SingleLivery), Strings.StatsCarsSingle, Strings.StatsCarsSingleDetail, null, null),
        ];

        // The calculator orders by liveries (then cars, then name); the other orders keep that as the tie-break
        IEnumerable<CarCoverageRow> ordered = coverage.Manufacturers.Where(r => r.Liveries > 0);
        ordered = CoverageOrder switch
        {
            CoverageOrder.Cars => ordered.OrderByDescending(r => r.Covered),
            CoverageOrder.Share => ordered.OrderByDescending(r => r.Share).ThenByDescending(r => r.Covered),
            _ => ordered,
        };
        var withLiveries = ordered.ToList();
        var shown = withLiveries.Take(MakesShown).ToList();
        var rows = shown.Select(row =>
        {
            var sample = _items.FirstOrDefault(i => i.CarKnown && string.Equals(i.CarManufacturerRaw, row.Key, StringComparison.OrdinalIgnoreCase));
            var command = sample is null ? null : new RelayCommand(() => _host.ApplyQuickFilter(QuickFilter.ForManufacturer(sample)));
            return new ShareRowViewModel(row.Label, $"{N(row.Covered)} / {N(row.Cars)}", Percent(row.Share), row.Share, command,
                command is null ? null : Strings.StatsRowTooltip, LiveryCount(row.Liveries));
        }).ToList();
        int moreWith = withLiveries.Count - shown.Count;
        int without = coverage.Manufacturers.Count - withLiveries.Count;
        string? more = moreWith > 0 || without > 0
            ? string.Format(Strings.StatsCoverageMoreFormat, N(moreWith), N(without))
            : null;
        CoverageCard = new BreakdownCardViewModel(Strings.StatsCoverageTitle, rows, more, rows.Count == 0 ? Strings.StatsNothing : null);
    }
}

/// <summary>The order of the coverage card's manufacturers</summary>
internal enum CoverageOrder
{
    /// <summary>Most liveries first</summary>
    Liveries,

    /// <summary>Most cars with liveries first</summary>
    Cars,

    /// <summary>Largest share of the cars with liveries first</summary>
    Share,
}
