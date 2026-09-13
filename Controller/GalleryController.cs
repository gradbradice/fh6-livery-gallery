using Avalonia.Controls;
using LiveryGallery.Enums;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.Controller;

internal sealed class GalleryController(Control groupsHost, Control galleryScroll)
{
    public List<LiveryEntry> AllEntries { get; set; } = [];
    public ObservableCollection<LiveryGroup> DisplayedGroups { get; } = [];
    public HashSet<string> SelectedTags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<LiveryEntry> GetFilteredEntries(
        string? searchText, FavoriteMode favoriteMode, DuplicatesFilterMode duplicatesFilterMode) =>
        GalleryFilterService.Apply(
            AllEntries, searchText, SelectedTags, favoriteMode == FavoriteMode.OnlyFavorites, duplicatesFilterMode);

    public List<LiveryGroup> BuildGroups(
        List<LiveryEntry> filtered, SortMode sortMode, FavoriteMode favoriteMode, bool groupingEnabled, double groupWidth) =>
        GalleryGroupingService.Group(filtered, sortMode, favoriteMode, groupingEnabled, groupWidth);

    public void ReplaceGroups(List<LiveryGroup> newGroups)
    {
        bool samePositionalOrder = DisplayedGroups.Count == newGroups.Count;
        if (samePositionalOrder)
        {
            for (int i = 0; i < newGroups.Count; i++)
            {
                if (DisplayedGroups[i].Key != newGroups[i].Key)
                {
                    samePositionalOrder = false;
                    break;
                }
            }
        }

        if (!samePositionalOrder)
        {
            foreach (var oldGroup in DisplayedGroups)
                oldGroup.Dispose();
            DisplayedGroups.Clear();
            foreach (var newGroup in newGroups)
                DisplayedGroups.Add(newGroup);

            groupsHost.InvalidateMeasure();
            galleryScroll.InvalidateMeasure();
            return;
        }

        for (int i = 0; i < newGroups.Count; i++)
        {
            var oldGroup = DisplayedGroups[i];
            var newGroup = newGroups[i];

            if (AreGroupsEquivalent(oldGroup, newGroup))
            {
                newGroup.Dispose();
                continue;
            }

            oldGroup.Dispose();
            DisplayedGroups[i] = newGroup;
        }

        groupsHost.InvalidateMeasure();
        galleryScroll.InvalidateMeasure();
    }

    private static bool AreGroupsEquivalent(LiveryGroup oldGroup, LiveryGroup newGroup)
    {
        if (oldGroup.Items.Count != newGroup.Items.Count) return false;
        for (int i = 0; i < oldGroup.Items.Count; i++)
        {
            if (!AreEntriesEquivalent(oldGroup.Items[i], newGroup.Items[i])) return false;
        }
        return true;
    }

    public List<LiveryEntry> MergeWithLocalState(List<LiveryEntry> freshEntries)
    {
        var previousByPath = AllEntries.ToDictionary(e => e.FolderPath);
        var mergedEntries = new List<LiveryEntry>(freshEntries.Count);
        foreach (var newEntry in freshEntries)
        {
            if (previousByPath.TryGetValue(newEntry.FolderPath, out var previous))
            {
                newEntry.IsFavorite = previous.IsFavorite;
                newEntry.Tags = previous.Tags;

                if (AreEntriesEquivalent(previous, newEntry))
                {
                    mergedEntries.Add(previous);
                    continue;
                }
            }
            mergedEntries.Add(newEntry);
        }
        return mergedEntries;
    }

    private static bool AreEntriesEquivalent(LiveryEntry a, LiveryEntry b)
    {
        return a.FolderPath == b.FolderPath
            && a.LiveryName == b.LiveryName
            && a.Author == b.Author
            && a.CarId == b.CarId
            && a.CarManufacturerRaw == b.CarManufacturerRaw
            && a.CarModelNameRaw == b.CarModelNameRaw
            && a.CarYear == b.CarYear
            && a.CarKnown == b.CarKnown
            && a.CreatedYear == b.CreatedYear
            && a.CreatedMonth == b.CreatedMonth
            && a.DownloadDate == b.DownloadDate
            && a.ThumbnailPath == b.ThumbnailPath
            && a.DuplicateStatus == b.DuplicateStatus
            && a.CLiveryHash == b.CLiveryHash
            && SectionCountsEqual(a.SectionCounts, b.SectionCounts);
    }

    private static bool SectionCountsEqual(IReadOnlyList<uint>? a, IReadOnlyList<uint>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.SequenceEqual(b);
    }
}
