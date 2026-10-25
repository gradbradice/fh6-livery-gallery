using ForzaToolkit.Liveries.Cache;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Services;

/// <summary>Why a copy of a duplicated livery stays in the save</summary>
internal enum FreeSpaceStay
{
    /// <summary>The copy kept for the group: the best of those that may go</summary>
    Best,

    /// <summary>Locked: never goes to the archive</summary>
    Locked,

    /// <summary>On a car: the game would lose it</summary>
    Installed,

    /// <summary>An auction livery: goes only with a list of applied liveries from the save as it is now</summary>
    Auction,

    /// <summary>A favorite: the user chose it, the planner does not send it away</summary>
    Favorite,
}

/// <summary>A copy that stays, and why</summary>
internal sealed record FreeSpaceKept(LiveryEntry Entry, FreeSpaceStay Reason);

/// <summary>The copies of one livery: those that stay (at least one) and those that may go</summary>
internal sealed record FreeSpaceGroup(IReadOnlyList<FreeSpaceKept> Stay, IReadOnlyList<LiveryEntry> Go);

/// <summary>
/// What "free up room" proposes: in every group of exact duplicates (the same design, car and paint) one copy stays, the
/// best one, and the others go to the archive. A copy that may not go (locked, on a car, an auction livery) or that the
/// user marked a favorite stays as well, and then is the one kept. Pure
/// </summary>
internal static class FreeSpacePlanner
{
    /// <summary>A relation with this score or more is an exact duplicate; a possible duplicate scores less</summary>
    private const double ExactScore = 0.9999;

    /// <param name="entries">The whole gallery</param>
    /// <param name="installedKnown">The list of applied liveries is there: a copy on no car is known to be free</param>
    /// <returns>The groups with something to free, biggest first</returns>
    public static IReadOnlyList<FreeSpaceGroup> Plan(IReadOnlyCollection<LiveryEntry> entries, bool installedKnown)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var groups = new List<FreeSpaceGroup>();
        foreach (var copies in DuplicateGroups(entries))
        {
            var stay = new List<FreeSpaceKept>();
            var free = new List<LiveryEntry>();
            foreach (var copy in copies)
            {
                if (StayReason(copy, installedKnown) is { } reason) stay.Add(new(copy, reason));
                else free.Add(copy);
            }
            if (stay.Count == 0)
            {
                var best = free.OrderBy(e => e, BestFirst.Instance).First();
                free.Remove(best);
                stay.Add(new(best, FreeSpaceStay.Best));
            }
            if (free.Count > 0) groups.Add(new FreeSpaceGroup(stay, [.. free.OrderBy(e => e, BestFirst.Instance)]));
        }
        return [.. groups.OrderByDescending(g => g.Go.Count).ThenBy(g => g.Stay[0].Entry.Data.CarId).ThenBy(g => g.Stay[0].Entry.FolderName, StringComparer.Ordinal)];
    }

    /// <summary>Why a copy may not go; null when it may</summary>
    private static FreeSpaceStay? StayReason(LiveryEntry copy, bool installedKnown) =>
        ArchiveRules.Check(copy.IsLocked, copy.IsAuction, copy.IsInstalled, installedKnown) switch
        {
            ArchiveBlock.Locked => FreeSpaceStay.Locked,
            ArchiveBlock.Installed => FreeSpaceStay.Installed,
            ArchiveBlock.AuctionNeedsInstalledList => FreeSpaceStay.Auction,
            // An auction livery is never proposed: it does not take a slot, so sending it away frees nothing
            _ when copy.IsAuction => FreeSpaceStay.Auction,
            _ when copy.IsFavorite => FreeSpaceStay.Favorite,
            _ => null,
        };

    /// <summary>
    /// The groups of exact duplicates: copies joined by relations that score as exact (<see cref="ExactScore"/>). A
    /// possible duplicate in between does not join two groups
    /// </summary>
    internal static IEnumerable<List<LiveryEntry>> DuplicateGroups(IReadOnlyCollection<LiveryEntry> entries)
    {
        var duplicates = entries.Where(e => e.IsDuplicate).ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < duplicates.Count; i++) index[duplicates[i].FolderName] = i;
        int[] parent = [.. Enumerable.Range(0, duplicates.Count)];
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        for (int i = 0; i < duplicates.Count; i++)
            foreach (string other in ExactDuplicatesOf(duplicates[i]))
                if (index.TryGetValue(other, out int j))
                    parent[Find(i)] = Find(j);
        return Enumerable.Range(0, duplicates.Count)
            .GroupBy(Find)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(i => duplicates[i]).ToList());
    }

    /// <summary>The keys of the liveries <paramref name="entry"/> is an exact copy of (the statistics group by them too)</summary>
    public static IEnumerable<string> ExactDuplicatesOf(LiveryEntry entry) =>
        entry.PossibleDuplicateOf?.Where(IsExact).Select(r => r.Id) ?? [];

    private static bool IsExact(DuplicateRelation relation) => relation.Score is >= ExactScore;

    /// <summary>
    /// The copy to keep first: one with tags (the user worked on it), the user's own, the first downloaded (the
    /// original; the others came later), then by number and key, so the choice is the same every time
    /// </summary>
    private sealed class BestFirst : IComparer<LiveryEntry>
    {
        public static readonly BestFirst Instance = new();

        public int Compare(LiveryEntry? x, LiveryEntry? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;
            int c = (y.Tags.Count > 0).CompareTo(x.Tags.Count > 0);
            if (c != 0) return c;
            c = y.IsMine.CompareTo(x.IsMine);
            if (c != 0) return c;
            c = Nullable.Compare(x.Data.DownloadDate, y.Data.DownloadDate) switch
            {
                // A copy without a date comes after one with a date
                _ when x.Data.DownloadDate is null && y.Data.DownloadDate is not null => 1,
                _ when x.Data.DownloadDate is not null && y.Data.DownloadDate is null => -1,
                var d => d,
            };
            if (c != 0) return c;
            c = (x.Data.LiveryId == 0).CompareTo(y.Data.LiveryId == 0);
            if (c != 0) return c;
            c = x.Data.LiveryId.CompareTo(y.Data.LiveryId);
            return c != 0 ? c : StringComparer.Ordinal.Compare(x.FolderName, y.FolderName);
        }
    }
}
