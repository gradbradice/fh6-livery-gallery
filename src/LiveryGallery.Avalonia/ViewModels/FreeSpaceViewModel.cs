using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>One copy in "free up room": one that stays (and why), or one that may go, ticked by default</summary>
internal sealed partial class FreeSpaceRowViewModel : ObservableObject, IThumbnailHost
{
    private readonly Action _changed;

    public FreeSpaceRowViewModel(LiveryEntry entry, FreeSpaceStay? stay, Action changed)
    {
        Entry = entry;
        Stay = stay;
        _changed = changed;
        _goes = stay is null;
    }

    public LiveryEntry Entry { get; }

    /// <summary>Why it stays; null for a copy that may go</summary>
    public FreeSpaceStay? Stay { get; }

    public bool CanGo => Stay is null;
    public bool Stays => Stay is not null;

    public string LiveryName => Entry.LiveryName;
    public string LiveryIdText => Entry.LiveryIdText;
    public string? ThumbnailPath => Entry.Data.ThumbnailPath;

    public string DetailText => Entry.Data.DownloadDate is { } date
        ? string.Format(Strings.FreeSpaceDownloadedFormat, date.ToString("d", AppLocalisationService.Culture))
        : Strings.UnknownDownloadDate;

    public string StayText => Stay switch
    {
        FreeSpaceStay.Best => Strings.FreeSpaceStayBest,
        FreeSpaceStay.Locked => Strings.FreeSpaceStayLocked,
        FreeSpaceStay.Installed => Strings.FreeSpaceStayInstalled,
        FreeSpaceStay.Auction => Strings.FreeSpaceStayAuction,
        FreeSpaceStay.Favorite => Strings.FreeSpaceStayFavorite,
        _ => "",
    };

    /// <summary>Ticked: goes to the archive. Only a copy that may go</summary>
    [ObservableProperty]
    private bool _goes;

    partial void OnGoesChanged(bool value) => _changed();

    [ObservableProperty]
    private Bitmap? _thumbnail;
}

/// <summary>The copies of one livery: the car and author, then the copies, the kept ones first</summary>
internal sealed class FreeSpaceGroupViewModel(string title, string countText, IReadOnlyList<FreeSpaceRowViewModel> rows)
{
    public string Title { get; } = title;
    public string CountText { get; } = countText;
    public IReadOnlyList<FreeSpaceRowViewModel> Rows { get; } = rows;
}

/// <summary>
/// "Free up room": the groups of exact duplicates with the copy that stays and those that go (<see cref="FreeSpacePlanner"/>).
/// The user may untick a copy; the button sends the ticked ones to the archive through the gallery's usual checks
/// </summary>
internal sealed partial class FreeSpaceViewModel : ObservableObject
{
    private readonly int _counted;
    private readonly int? _limit;

    /// <param name="plan">The planner's groups</param>
    /// <param name="counted">The liveries the limit counts now</param>
    /// <param name="limit">The save's limit; null when the game has none</param>
    public FreeSpaceViewModel(IReadOnlyList<FreeSpaceGroup> plan, int counted, int? limit)
    {
        _counted = counted;
        _limit = limit;
        var culture = AppLocalisationService.Culture;
        Groups =
        [
            .. plan.Select(g =>
            {
                var first = g.Stay[0].Entry;
                string title = $"{first.CarManufacturer} {first.CarModelName}".Trim() is { Length: > 0 } car
                    ? $"{car} · {first.Author}" : first.Author;
                int copies = g.Stay.Count + g.Go.Count;
                return new FreeSpaceGroupViewModel(title, string.Format(Strings.FreeSpaceCopiesFormat, copies.ToString("N0", culture)),
                [
                    .. g.Stay.Select(s => new FreeSpaceRowViewModel(s.Entry, s.Reason, Recount)),
                    .. g.Go.Select(e => new FreeSpaceRowViewModel(e, null, Recount)),
                ]);
            }),
        ];
        Recount();
    }

    public IReadOnlyList<FreeSpaceGroupViewModel> Groups { get; }

    public bool IsEmpty => Groups.Count == 0;
    public bool HasGroups => Groups.Count > 0;

    /// <summary>The ticked copies</summary>
    public IReadOnlyList<LiveryEntry> ToArchive => [.. Groups.SelectMany(g => g.Rows).Where(r => r.CanGo && r.Goes).Select(r => r.Entry)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private int _goingCount;

    public bool CanApply => GoingCount > 0;

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private string _applyText = "";

    public string EmptyText => Strings.FreeSpaceNothing;

    private void Recount()
    {
        var culture = AppLocalisationService.Culture;
        GoingCount = Groups.SelectMany(g => g.Rows).Count(r => r.CanGo && r.Goes);
        string going = GoingCount.ToString("N0", culture);
        string now = _limit is { } limit
            ? string.Format(Strings.FreeSpaceNowFormat, _counted.ToString("N0", culture), limit.ToString("N0", culture))
            : "";
        string after = _limit is { } l
            ? string.Format(Strings.FreeSpaceAfterFormat, going, Math.Max(0, l - _counted + GoingCount).ToString("N0", culture))
            : string.Format(Strings.FreeSpaceAfterNoLimitFormat, going);
        SummaryText = now.Length > 0 ? $"{now} {after}" : after;
        ApplyText = string.Format(Strings.FreeSpaceApplyFormat, going);
    }

    public void SetAll(bool goes)
    {
        foreach (var row in Groups.SelectMany(g => g.Rows).Where(r => r.CanGo)) row.Goes = goes;
    }
}
