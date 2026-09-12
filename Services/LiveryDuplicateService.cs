using LiveryGallery.Enums;
using LiveryGallery.Models;

namespace LiveryGallery.Services;

internal static class LiveryDuplicateService
{
    private const int SectionCount = 11;
    private const int MinSharedForCandidate = 2;
    private const int MaxCandidatesPerKey = 300;
    private const double MinSectionMatchRatio = 0.6;
    private const int MinRelevantSections = 3;

    public static void MarkDuplicates(List<LiveryEntry> entries)
    {
        var exactGroups = entries
            .Where(e => e.CLiveryHash is not null)
            .GroupBy(e => (e.CarId, e.Author, e.CLiveryHash), CarIdAuthorHashComparer.Instance);

        foreach (var group in exactGroups)
        {
            if (group.Count() <= 1) continue;
            foreach (var entry in group)
                entry.DuplicateStatus = DuplicateStatus.Duplicate;
        }

        var candidateGroups = entries
            .Where(e => e.DuplicateStatus != DuplicateStatus.Duplicate && e.SectionCounts is { Count: SectionCount })
            .GroupBy(e => (e.CarId, e.Author), CarIdAuthorComparer.Instance);

        foreach (var group in candidateGroups)
            MarkPossibleDuplicatesInGroup([.. group]);
    }

    private sealed class CarIdAuthorHashComparer : IEqualityComparer<(int CarId, string Author, string? CLiveryHash)>
    {
        public static readonly CarIdAuthorHashComparer Instance = new();

        public bool Equals((int CarId, string Author, string? CLiveryHash) x, (int CarId, string Author, string? CLiveryHash) y) =>
            x.CarId == y.CarId
            && string.Equals(x.Author, y.Author, StringComparison.OrdinalIgnoreCase)
            && x.CLiveryHash == y.CLiveryHash;

        public int GetHashCode((int CarId, string Author, string? CLiveryHash) obj) =>
            HashCode.Combine(obj.CarId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Author), obj.CLiveryHash);
    }

    private sealed class CarIdAuthorComparer : IEqualityComparer<(int CarId, string Author)>
    {
        public static readonly CarIdAuthorComparer Instance = new();

        public bool Equals((int CarId, string Author) x, (int CarId, string Author) y) =>
            x.CarId == y.CarId && string.Equals(x.Author, y.Author, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int CarId, string Author) obj) =>
            HashCode.Combine(obj.CarId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Author));
    }

    private static void MarkPossibleDuplicatesInGroup(List<LiveryEntry> items)
    {
        var index = new Dictionary<(int Position, uint Value), List<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            var counts = items[i].SectionCounts!;
            for (int pos = 0; pos < counts.Count; pos++)
            {
                if (counts[pos] == 0) continue;
                var key = (pos, counts[pos]);
                if (!index.TryGetValue(key, out var list))
                    index[key] = list = [];
                if (list.Count < MaxCandidatesPerKey)
                    list.Add(i);
            }
        }

        var sharedCounts = new Dictionary<int, int>();

        for (int i = 0; i < items.Count; i++)
        {
            var counts = items[i].SectionCounts!;
            sharedCounts.Clear();
            bool hasAnyShared = false;

            for (int pos = 0; pos < counts.Count; pos++)
            {
                if (counts[pos] == 0) continue;
                if (!index.TryGetValue((pos, counts[pos]), out var candidates)) continue;

                foreach (int j in candidates)
                {
                    if (j <= i) continue;
                    hasAnyShared = true;
                    sharedCounts[j] = sharedCounts.GetValueOrDefault(j) + 1;
                }
            }

            if (!hasAnyShared) continue;

            foreach (var (j, shared) in sharedCounts)
            {
                if (shared < MinSharedForCandidate) continue;
                if (AreSectionsSimilar(items[i].SectionCounts!, items[j].SectionCounts!))
                {
                    items[i].DuplicateStatus = DuplicateStatus.PossibleDuplicate;
                    items[j].DuplicateStatus = DuplicateStatus.PossibleDuplicate;
                }
            }
        }
    }

    private static bool AreSectionsSimilar(IReadOnlyList<uint> a, IReadOnlyList<uint> b)
    {
        int relevant = 0, matches = 0;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] == 0 && b[i] == 0) continue;
            relevant++;
            if (a[i] == b[i]) matches++;
        }

        if (relevant < MinRelevantSections) return false;
        return (double)matches / relevant >= MinSectionMatchRatio;
    }
}
