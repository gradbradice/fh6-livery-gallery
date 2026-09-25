using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class GalleryViewModel : ObservableObject
{
    private readonly FavoriteService favoriteService;
    private List<LiveryEntry> _allEntries = [];
    public IReadOnlyList<LiveryEntry> AllEntries => _allEntries;
    public ObservableCollection<LiveryGroup> DisplayedGroups { get; } = [];
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> SelectedTags => _selectedTags;
    private readonly HashSet<string> _selectedFolderNames = new(StringComparer.Ordinal);
    public ObservableCollection<LiveryEntry> SelectedEntries { get; } = [];
    public bool HasSelection => SelectedEntries.Count > 0;
    public int SelectedCount => SelectedEntries.Count;
    public string SelectionSummaryText => string.Format(Strings.SelectedCountLabel, SelectedCount);
    private GalleryFilterState _filter;

    private double _groupWidth = 1200;
    public double GroupWidth
    {
        get => _groupWidth;
        set
        {
            if (_groupWidth == value) return;
            _groupWidth = value;
            ApplyGroupWidthToDisplayedGroups();
        }
    }

    public event Action? GroupsReplaced;
    public event Action<LiveryEntry>? AuthorRowRequested;
    public event Action<LiveryEntry>? EditTagsRequested;
    public event Action<LiveryEntry>? ViewPreviewRequested;
    public event Action<GalleryCountsSnapshot>? CountsUpdated;

    public GalleryViewModel(FavoriteService favoriteService)
    {
        this.favoriteService = favoriteService;
        SelectedEntries.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionSummaryText));
        };
    }

    public List<LiveryEntry> GetFilteredEntries() =>
        GalleryFilterService.Apply(
            _allEntries, _filter.SearchText, _selectedTags,
            _filter.FavoriteMode == FavoriteMode.OnlyFavorites, _filter.MineMode == MineMode.OnlyMine,
            _filter.DuplicatesFilterMode, _filter.GeneratedFilterMode, _filter.PaintFilterMode,
            _filter.SearchByFolderName);

    [RelayCommand]
    private void ToggleFavorite(LiveryEntry entry)
    {
        entry.IsFavorite = !entry.IsFavorite;
        favoriteService.SetFavorite(entry.FolderName, entry.IsFavorite);

        if (_filter.FavoriteMode != FavoriteMode.None)
            Refresh(_filter);
        else
            RefreshCountsOnly();
    }

    [RelayCommand]
    private void ShowAuthorRow(LiveryEntry entry) => AuthorRowRequested?.Invoke(entry);

    [RelayCommand]
    private void EditTags(LiveryEntry entry) => EditTagsRequested?.Invoke(entry);

    public void RefreshDuplicateTooltips()
    {
        foreach (var entry in AllEntries) entry.RefreshDuplicateTooltip();
    }

    [RelayCommand]
    private void ViewPreview(LiveryEntry entry) => ViewPreviewRequested?.Invoke(entry);

    public void SetTagSelected(string tag, bool selected)
    {
        if (selected) _selectedTags.Add(tag);
        else _selectedTags.Remove(tag);
        Refresh(_filter);
    }

    [RelayCommand]
    private void SelectTag(string tag) => SetTagSelected(tag, true);

    public void SyncKnownTags(IReadOnlyCollection<string> knownTags)
    {
        if (_selectedTags.Count == 0) return;
        _selectedTags.RemoveWhere(t => !knownTags.Contains(t));
    }

    public void Refresh(GalleryFilterState filter)
    {
        _filter = filter;

        bool suppressCardBadge = filter.GroupingEnabled
            && (filter.SortMode == SortMode.Author || filter.MineMode == MineMode.MineSeparately);
        foreach (var entry in _allEntries)
            entry.ShowMineBadge = entry.IsMine && !suppressCardBadge;

        var filtered = GetFilteredEntries();
        var groups = GalleryGroupingService.Group(
            filtered, filter.SortMode, filter.FavoriteMode, filter.MineMode,
            filter.GroupingEnabled, GroupWidth);
        ReplaceGroups(groups);
        CountsUpdated?.Invoke(new GalleryCountsSnapshot(filtered, _allEntries.Count, filter.SearchText));
    }

    private void ApplyGroupWidthToDisplayedGroups()
    {
        foreach (var group in DisplayedGroups)
            group.GroupWidth = GroupWidth;
    }

    public void RefreshCountsOnly() =>
        CountsUpdated?.Invoke(new GalleryCountsSnapshot(GetFilteredEntries(), _allEntries.Count, _filter.SearchText));

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

            GroupsReplaced?.Invoke();
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

        GroupsReplaced?.Invoke();
    }

    private static bool AreGroupsEquivalent(LiveryGroup oldGroup, LiveryGroup newGroup)
    {
        if (oldGroup.Items.Count != newGroup.Items.Count) return false;
        for (int i = 0; i < oldGroup.Items.Count; i++)
        {
            if (!HasEquivalentPresentationData(oldGroup.Items[i], newGroup.Items[i])) return false;
        }
        return true;
    }

    public void ReplaceEntries(List<LiveryEntry> entries)
    {
        _allEntries = entries;
        ReconcileSelection();
    }

    private void ReconcileSelection()
    {
        if (_selectedFolderNames.Count == 0) return;

        var byName = _allEntries.ToDictionary(e => e.FolderName);
        _selectedFolderNames.RemoveWhere(name => !byName.ContainsKey(name));

        SelectedEntries.Clear();
        foreach (var name in _selectedFolderNames)
        {
            var entry = byName[name];
            entry.IsSelected = true;
            SelectedEntries.Add(entry);
        }
    }

    [RelayCommand]
    private void ToggleSelection(LiveryEntry entry)
    {
        if (_selectedFolderNames.Add(entry.FolderName))
        {
            entry.IsSelected = true;
            SelectedEntries.Add(entry);
            return;
        }

        _selectedFolderNames.Remove(entry.FolderName);
        entry.IsSelected = false;
        var existing = SelectedEntries.FirstOrDefault(e => e.FolderName == entry.FolderName);
        if (existing is not null) SelectedEntries.Remove(existing);
    }

    [RelayCommand]
    private void SelectOnly(LiveryEntry entry)
    {
        foreach (var e in SelectedEntries) e.IsSelected = false;
        _selectedFolderNames.Clear();
        SelectedEntries.Clear();

        entry.IsSelected = true;
        _selectedFolderNames.Add(entry.FolderName);
        SelectedEntries.Add(entry);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (_selectedFolderNames.Count == 0) return;
        foreach (var entry in SelectedEntries) entry.IsSelected = false;
        _selectedFolderNames.Clear();
        SelectedEntries.Clear();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var entry in SelectedEntries) entry.IsSelected = false;
        _selectedFolderNames.Clear();
        SelectedEntries.Clear();
        foreach (var entry in GetFilteredEntries())
        {
            entry.IsSelected = true;
            _selectedFolderNames.Add(entry.FolderName);
            SelectedEntries.Add(entry);
        }
    }

    public List<LiveryEntry> MergeWithLocalState(List<LiveryEntry> freshEntries)
    {
        var previousByName = _allEntries.ToDictionary(e => e.FolderName);
        var mergedEntries = new List<LiveryEntry>(freshEntries.Count);
        foreach (var newEntry in freshEntries)
        {
            if (previousByName.TryGetValue(newEntry.FolderName, out var previous))
            {
                newEntry.IsFavorite = previous.IsFavorite;
                newEntry.Tags = previous.Tags;

                if (HasEquivalentPresentationData(previous, newEntry))
                {
                    mergedEntries.Add(previous);
                    continue;
                }
            }
            mergedEntries.Add(newEntry);
        }
        return mergedEntries;
    }

    private static bool HasEquivalentPresentationData(LiveryEntry a, LiveryEntry b)
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
            && a.HasNoLayers == b.HasNoLayers
            && a.HasParseError == b.HasParseError
            && a.LiveryId == b.LiveryId
            && a.CLiveryHash == b.CLiveryHash;
    }

    private static bool PossibleDuplicateOfEqual(IReadOnlyList<DuplicateRelation>? a, IReadOnlyList<DuplicateRelation>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || a.Count == 0) return b is null || b.Count == 0;
        if (b is null || a.Count != b.Count) return false;
        return a.ToHashSet().SetEquals(b);
    }
}
