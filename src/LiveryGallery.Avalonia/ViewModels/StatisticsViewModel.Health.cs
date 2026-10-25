using CommunityToolkit.Mvvm.ComponentModel;
using ForzaToolkit.Liveries.Statistics;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>A card of the health page: a few facts, each leading to its liveries</summary>
internal sealed record HealthCardViewModel(string Title, string? Note, IReadOnlyList<FactViewModel> Facts);

internal sealed partial class StatisticsViewModel
{
    private static readonly TimeSpan BackupsRereadAfter = TimeSpan.FromMinutes(2);

    private IReadOnlyList<LiveryBackupManifest?>? _backups;
    private DateTime _backupsReadUtc;
    private Task? _accountsLoading;

    [ObservableProperty]
    private IReadOnlyList<StatTileViewModel> _tiles = [];

    [ObservableProperty]
    private bool _hasLimit;

    [ObservableProperty]
    private string _slotsText = "";

    [ObservableProperty]
    private string _slotsDetail = "";

    [ObservableProperty]
    private double _slotsFraction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlotsWarning), nameof(IsSlotsFull))]
    private SlotsLevel _slotsLevel;

    public bool IsSlotsWarning => SlotsLevel == SlotsLevel.Warning;
    public bool IsSlotsFull => SlotsLevel == SlotsLevel.Full;

    [ObservableProperty]
    private IReadOnlyList<FactViewModel> _healthSummary = [];

    [ObservableProperty]
    private IReadOnlyList<HealthCardViewModel> _healthCards = [];

    [ObservableProperty]
    private IReadOnlyList<AccountSummary> _accounts = [];

    [ObservableProperty]
    private bool _hasAccounts;

    private void BuildOverview()
    {
        var recent = LiveryTimelineCalculator.Summarize(_events, DateTime.UtcNow - RecentPeriod, _timelineOptions);
        var o = LiveryStatistics.Overview(_items, _host.Source.AuthorKey);
        bool installedKnown = _host.IsInstalledKnown();
        Tiles =
        [
            new(N(o.Liveries), Strings.StatsTileLiveries,
                _events.Count > 0 ? string.Format(Strings.StatsTileNewFormat, N(recent.Arrivals)) : "",
                Strings.StatsTileLiveriesTooltip, Go(StatisticsTarget.Everything)),
            new(N(o.Authors), Strings.StatsTileAuthors, string.Format(Strings.StatsTileWithoutAuthorFormat, N(o.WithoutAuthor)),
                Strings.StatsTileAuthorsTooltip, Go(StatisticsTarget.ByAuthor)),
            new(N(o.Cars), Strings.StatsTileCars, string.Format(Strings.StatsTileManufacturersFormat, N(o.Manufacturers)),
                Strings.StatsTileCarsTooltip, Go(StatisticsTarget.ByManufacturer)),
            new(N(o.Favorites), Strings.StatsTileFavorites, Share(o.Favorites, o.Liveries),
                Strings.StatsTileFavoritesTooltip, Go(StatisticsTarget.Favorites)),
            installedKnown
                ? new(N(o.Installed), Strings.StatsTileInstalled, string.Format(Strings.StatsTileInstalledCarsFormat, N(o.InstalledCars)),
                    Strings.StatsTileInstalledTooltip, Go(StatisticsTarget.Installed))
                : new("—", Strings.StatsTileInstalled, Strings.StatsTileInstalledUnknown, Strings.InstalledFilterNeedsServer, null),
            new(N(o.Auction), Strings.StatsTileAuction, Share(o.Auction, o.Liveries),
                Strings.StatsTileAuctionTooltip, Go(StatisticsTarget.Auction)),
        ];

        var health = Health();
        UpdateSlots(health);
        var open = new CommunityToolkit.Mvvm.Input.RelayCommand(() => Page = StatisticsPage.Health);
        var summary = new List<FactViewModel>();
        if (health.NotBackedUp is { } notBackedUp)
            summary.Add(new(Strings.StatsHealthNotBackedUp, N(notBackedUp.Count), open));
        summary.Add(new(Strings.StatsHealthFreeable, N(health.FreeableByDuplicates), open));
        if (health.Unused is { } unused) summary.Add(new(Strings.StatsHealthUnused, N(unused.Count), open));
        HealthSummary = summary;

        if (_accountsLoading is null && _host.Accounts is { } accounts)
            _accountsLoading = LoadAccountsAsync(accounts);
    }

    private async Task LoadAccountsAsync(Func<Task<IReadOnlyList<AccountSummary>>> load)
    {
        try
        {
            var list = await load();
            Accounts = list;
            HasAccounts = list.Count > 1;
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to read the other accounts for the statistics", ex);
        }
    }

    /// <summary>The health of the whole account (the limit is the save's, whatever the scope)</summary>
    private LiveryHealth Health()
    {
        var backups = Backups();
        HashSet<string>? backedUp = null;
        DateTime? lastBackup = null;
        if (backups is not null)
        {
            backedUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in backups)
            {
                if (manifest is null) continue;
                if (lastBackup is null || manifest.CreatedAtUtc > lastBackup) lastBackup = manifest.CreatedAtUtc;
                foreach (var data in manifest.Entries)
                    backedUp.Add(data.CLiveryHash is { Length: > 0 } hash ? hash : "key:" + data.FolderName);
            }
        }
        var archived = _host.Archive?.Invoke() ?? [];
        return LiveryHealthCalculator.Calculate(_all, new LiveryHealthOptions<LiveryEntry>
        {
            Limit = _host.MaxLiveries,
            IsBackedUp = backedUp is null ? null
                : e => backedUp.Contains(e.CLiveryHash is { Length: > 0 } hash ? hash : "key:" + e.FolderName),
            LastBackupUtc = lastBackup,
            InstalledKnown = _host.IsInstalledKnown(),
            // Exact copies only: a possible duplicate between two groups does not make them one ("free up room" counts so)
            DuplicatesOf = FreeSpacePlanner.ExactDuplicatesOf,
            ArchivedCount = archived.Count,
            LastArchivedUtc = archived.Count == 0 ? null : archived.Max(a => a.ArchivedAtUtc),
        });
    }

    private IReadOnlyList<LiveryBackupManifest?>? Backups()
    {
        if (_host.Backups is not { } read) return null;
        if (_backups is null || DateTime.UtcNow - _backupsReadUtc > BackupsRereadAfter)
        {
            try
            {
                _backups = read();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AppLog.LogError("Failed to read the backups for the statistics", ex);
                _backups = [];
            }
            _backupsReadUtc = DateTime.UtcNow;
        }
        return _backups;
    }

    private void UpdateSlots(LiveryHealth health)
    {
        HasLimit = health.Limit is not null;
        if (health.Limit is not { } limit) return;
        double share = health.UsedShare ?? 0;
        SlotsFraction = share;
        SlotsText = $"{N(health.Counted)} / {N(limit)}";
        SlotsLevel = SaveRoom.Level(health.Counted, limit); // the main window's counter takes the same colour
        SlotsDetail = SlotsLevel switch
        {
            SlotsLevel.Full => Strings.StatsSlotsFull,
            SlotsLevel.Warning => string.Format(Strings.StatsSlotsAlmostFormat, N(health.Free ?? 0)),
            _ => string.Format(Strings.StatsSlotsFreeFormat, N(health.Free ?? 0)),
        };
    }

    private void BuildHealth()
    {
        var health = Health();
        UpdateSlots(health);
        var cards = new List<HealthCardViewModel>();

        var backupFacts = new List<FactViewModel>();
        if (health.NotBackedUp is { } notBackedUp)
        {
            backupFacts.Add(new(Strings.StatsHealthNotBackedUp, N(notBackedUp.Count),
                Show(notBackedUp, Strings.StatsHealthNotBackedUp), Strings.StatsShowTheseTooltip));
            backupFacts.Add(new(Strings.StatsHealthLastBackup, Date(health.LastBackupUtc?.ToLocalTime()), null));
        }
        cards.Add(new HealthCardViewModel(Strings.StatsHealthBackupsTitle,
            health.NotBackedUp is null ? Strings.StatsHealthNoBackups
                : health.NotBackedUp.Count == 0 ? Strings.StatsHealthAllBackedUp : Strings.StatsHealthBackupsNote,
            backupFacts));

        cards.Add(new HealthCardViewModel(Strings.StatsHealthDuplicatesTitle, Strings.StatsHealthDuplicatesNote,
        [
            new(Strings.StatsHealthDuplicates, N(health.Duplicates.Count), Show(health.Duplicates, Strings.StatsHealthDuplicates), Strings.StatsShowTheseTooltip),
            new(Strings.StatsHealthFreeable, N(health.FreeableByDuplicates), null),
            new(Strings.StatsHealthPossible, N(health.PossibleDuplicates.Count), Show(health.PossibleDuplicates, Strings.StatsHealthPossible), Strings.StatsShowTheseTooltip),
        ]));

        cards.Add(new HealthCardViewModel(Strings.StatsHealthUnusedTitle,
            health.Unused is null ? Strings.InstalledFilterNeedsServer : Strings.StatsHealthUnusedNote,
            health.Unused is { } unused
                ? [new(Strings.StatsHealthUnused, N(unused.Count), Show(unused, Strings.StatsHealthUnused), Strings.StatsShowTheseTooltip)]
                : []));

        cards.Add(new HealthCardViewModel(Strings.StatsHealthArchiveTitle, null,
        [
            new(Strings.StatsHealthArchived, N(health.ArchivedCount), null),
            new(Strings.StatsHealthLastArchived, Date(health.LastArchivedUtc?.ToLocalTime()), null),
        ]));
        HealthCards = cards;
    }
}
