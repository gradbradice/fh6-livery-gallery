using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

internal sealed partial class StatisticsViewModel
{
    [ObservableProperty]
    private IReadOnlyList<StatTileViewModel> _layerTiles = [];

    [ObservableProperty]
    private BreakdownCardViewModel? _layerHistogramCard;

    [ObservableProperty]
    private BreakdownCardViewModel? _mostComplexCard;

    [ObservableProperty]
    private BreakdownCardViewModel? _layersByAuthorCard;

    [ObservableProperty]
    private IReadOnlyList<FactViewModel> _records = [];

    private void BuildLiveries()
    {
        var culture = AppLocalisationService.Culture;
        var layers = LiveryStatistics.Layers(_items, mostComplex: SmallListSize);
        LayerTiles =
        [
            new(layers.Median is { } median ? median.ToString("N0", culture) : "—", Strings.StatsLayersMedian, Strings.StatsLayersMedianDetail, null, null),
            new(layers.Max is { } max ? N(max) : "—", Strings.StatsLayersMax, Strings.StatsLayersMaxDetail, null, null),
            new(N(layers.Known), Strings.StatsLayersKnown, string.Format(Strings.StatsLayersKnownDetailFormat, N(_items.Count)), null, null),
        ];

        var buckets = layers.Buckets.Select((bucket, index) =>
        {
            var keys = _items.Where(i => i.LayerCount is { } n && n >= bucket.From && (bucket.To is null || n <= bucket.To))
                .Select(i => i.FolderName).ToList();
            // A range of one count ("0", no layers) is that count
            string label = bucket.To is { } to ? to == bucket.From ? N(to) : $"{N(bucket.From)}–{N(to)}" : $"{N(bucket.From)}+";
            return (Label: label, Count: bucket.Count, Keys: keys);
        }).ToList();
        var histogram = new BreakdownCardViewModel(Strings.StatsLayersTitle,
            Rows(buckets, b => b.Label, b => b.Count, layers.Known, b => Show(b.Keys, string.Format(Strings.StatsLayersFilterFormat, b.Label)),
                Strings.StatsShowTheseTooltip),
            null, layers.Known == 0 ? Strings.StatsLayersUnknown : null);

        var complex = new BreakdownCardViewModel(Strings.StatsMostComplexTitle,
            Rows(layers.MostComplex.Select(key => _byKey[key]), LiveryLabel, e => e.LayerCount ?? 0, 0,
                e => Show([e.FolderName], AuthorInsights.Label(e)), Strings.StatsShowTheseTooltip),
            null, Strings.StatsLayersUnknown);

        var byAuthor = AuthorStatsFor(_items).Rows
            .Where(r => r.Liveries >= MinLiveriesForShares && r.AverageLayers is not null)
            .OrderByDescending(r => r.AverageLayers).Take(SmallListSize).ToList();
        var authorCard = new BreakdownCardViewModel(Strings.StatsLayersByAuthorTitle,
            Rows(byAuthor, r => r.Name, r => (int)Math.Round(r.AverageLayers!.Value), 0, r => OpenAuthorCommand(r.SampleKey), Strings.StatsAuthorRowTooltip),
            null, Strings.StatsLayersUnknown);
        LayerHistogramCard = histogram;
        MostComplexCard = complex;
        LayersByAuthorCard = authorCard;

        var records = LiveryRecordsFinder.Find(_items);
        var facts = new List<FactViewModel>();
        void Fact(string label, string? key, string value)
        {
            if (key is null || !_byKey.TryGetValue(key, out var entry)) return;
            facts.Add(new FactViewModel(label, value, Show([key], AuthorInsights.Label(entry)), LiveryLabel(entry)));
        }
        Fact(Strings.StatsRecordOldest, records.OldestKey, Date(records.OldestDownload));
        Fact(Strings.StatsRecordNewest, records.NewestKey, Date(records.NewestDownload));
        Fact(Strings.StatsRecordOldestMade, records.OldestMadeKey, records.OldestMade?.ToString("Y", culture) ?? "—");
        Fact(Strings.StatsRecordMostLayers, records.MostComplexKey, records.MostLayers is { } most ? N(most) : "—");
        if (records.BusiestCarSampleKey is { } carKey && _byKey.TryGetValue(carKey, out var carSample))
            facts.Add(new FactViewModel(Strings.StatsRecordBusiestCar, $"{carSample.CarManufacturer} {carSample.CarModelName} · {N(records.BusiestCarCount)}",
                new RelayCommand(() => _host.ApplyQuickFilter(QuickFilter.ForCar(carSample))), Strings.StatsRowTooltip));
        if (records.BusiestMonth is { } month)
            facts.Add(new FactViewModel(Strings.StatsRecordBusiestMonth, $"{month.ToString("Y", culture)} · {N(records.BusiestMonthCount)}", null));
        Records = facts;
    }

    private static string LiveryLabel(LiveryEntry entry) =>
        $"{AuthorInsights.Label(entry)} — {entry.CarManufacturer} {entry.CarModelName}".Trim();
}
