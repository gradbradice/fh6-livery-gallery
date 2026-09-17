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
        string? searchText, FavoriteMode favoriteMode, MineMode mineMode,
        DuplicatesFilterMode duplicatesFilterMode, GeneratedFilterMode generatedFilterMode) =>
        GalleryFilterService.Apply(
            AllEntries, searchText, SelectedTags,
            favoriteMode == FavoriteMode.OnlyFavorites, mineMode == MineMode.OnlyMine,
            duplicatesFilterMode, generatedFilterMode);

    public List<LiveryGroup> BuildGroups(
        List<LiveryEntry> filtered, SortMode sortMode, FavoriteMode favoriteMode, MineMode mineMode,
        bool groupingEnabled, double groupWidth) =>
        GalleryGroupingService.Group(filtered, sortMode, favoriteMode, mineMode, groupingEnabled, groupWidth);

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
        var previousByName = AllEntries.ToDictionary(e => e.FolderName);
        var mergedEntries = new List<LiveryEntry>(freshEntries.Count);
        foreach (var newEntry in freshEntries)
        {
            if (previousByName.TryGetValue(newEntry.FolderName, out var previous))
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
        return a.FolderName == b.FolderName
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
            && PossibleDuplicateOfEqual(a.PossibleDuplicateOf, b.PossibleDuplicateOf)
            && a.IsMine == b.IsMine
            && a.IsPossiblyGenerated == b.IsPossiblyGenerated
            && a.CLiveryHash == b.CLiveryHash;
    }

    private static bool PossibleDuplicateOfEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (a is null || a.Count == 0) return b is null || b.Count == 0;
        if (b is null || a.Count != b.Count) return false;
        return a.ToHashSet(StringComparer.Ordinal).SetEquals(b);
    }
}
