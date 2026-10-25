using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveryGallery.Localisation;
using LiveryGallery.Services;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class GalleryViewModel : ObservableObject
{
    private readonly FavoriteService favoriteService;
    private readonly LockService lockService;
    private List<LiveryEntry> _allEntries = [];
    public IReadOnlyList<LiveryEntry> AllEntries => _allEntries;
    public ObservableCollection<LiveryGroup> DisplayedGroups { get; } = [];
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedTags = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tags that hide a livery</summary>
    public IReadOnlySet<string> ExcludedTags => _excludedTags;
    public bool HasTagFilter => _selectedTags.Count > 0 || _excludedTags.Count > 0;
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
    /// <summary>The liveries whose tags to edit: one, or every selected one</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? EditTagsRequested;
    public event Action<LiveryEntry>? ViewPreviewRequested;
    public event Action<LiveryEntry>? View3DRequested;
    public event Action<GalleryCountsSnapshot>? CountsUpdated;
    public event Action? SelectedTagsChanged;
    /// <summary>A livery was locked or unlocked: what may go to the archive changed</summary>
    public event Action? LocksChanged;

    public GalleryViewModel(FavoriteService favoriteService, LockService lockService)
    {
        this.favoriteService = favoriteService;
        this.lockService = lockService;
        SelectedEntries.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionSummaryText));
            OnSelectionMenuChanged();
        };
    }

    /// <summary>
    /// What a card's action works on: every selected livery when the card is one of several selected, else the card
    /// alone. The context menu selects its card first, so there it is the selection. No card (a menu whose card was
    /// reused for another livery while it was open: the gallery regrouped meanwhile) — the selection
    /// </summary>
    public IReadOnlyList<LiveryEntry> TargetsOf(LiveryEntry? entry) =>
        entry is null || (entry.IsSelected && SelectedEntries.Count > 1) ? [.. SelectedEntries] : [entry];

    private int MenuCount => Math.Max(1, SelectedEntries.Count);

    private static string WithCount(string text, int count) => count > 1 ? $"{text} ({count})" : text;

    /// <summary>Every selected livery is a favorite: the context menu offers to remove them</summary>
    public bool FavoriteMenuRemoves => SelectedEntries.Count > 0 && SelectedEntries.All(e => e.IsFavorite);

    public string FavoriteMenuHeader => WithCount(
        FavoriteMenuRemoves ? Strings.ContextMenuRemoveFromFavorites : Strings.ContextMenuAddToFavorites, MenuCount);

    /// <summary>Every selected livery is locked: the context menu offers to unlock them</summary>
    public bool LockMenuRemoves => SelectedEntries.Count > 0 && SelectedEntries.All(e => e.IsLocked);

    public string LockMenuHeader => WithCount(LockMenuRemoves ? Strings.ContextMenuUnlock : Strings.ContextMenuLock, MenuCount);

    public string EditTagsMenuHeader => WithCount(Strings.ContextMenuEditTags, MenuCount);

    private void OnSelectionMenuChanged()
    {
        OnPropertyChanged(nameof(FavoriteMenuRemoves));
        OnPropertyChanged(nameof(FavoriteMenuHeader));
        OnPropertyChanged(nameof(LockMenuRemoves));
        OnPropertyChanged(nameof(LockMenuHeader));
        OnPropertyChanged(nameof(EditTagsMenuHeader));
        OnPropertyChanged(nameof(AssignAuthorMenuVisible));
        OnPropertyChanged(nameof(AssignAuthorMenuHeader));
        OnPropertyChanged(nameof(UnassignAuthorMenuVisible));
        OnPropertyChanged(nameof(UnassignAuthorMenuHeader));
    }

    public List<LiveryEntry> GetFilteredEntries() =>
        GalleryFilterService.Apply(
            _allEntries, _filter.SearchText, _selectedTags, _excludedTags,
            _filter.FavoriteMode == FavoriteMode.OnlyFavorites, _filter.MineMode == MineMode.OnlyMine,
            _filter.DuplicatesFilterMode, _filter.GeneratedFilterMode, _filter.PaintFilterMode,
            _filter.SearchByFolderName, _filter.QuickFilters,
            auctionFilterMode: _filter.AuctionFilterMode,
            onlyInstalled: _filter.InstalledMode == InstalledMode.OnlyInstalled);

    /// <summary>The star on a card: that livery alone</summary>
    [RelayCommand]
    private void ToggleFavorite(LiveryEntry entry) => SetFavorites([entry], !entry.IsFavorite);

    /// <summary>
    /// The context menu: every selected livery becomes a favorite, or, when all of them are already, none of them is
    /// </summary>
    [RelayCommand]
    private void ToggleFavorites(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry);
        if (targets.Count == 0) return;
        SetFavorites(targets, targets.Any(e => !e.IsFavorite));
    }

    private void SetFavorites(IReadOnlyList<LiveryEntry> entries, bool favorite)
    {
        bool changed = false;
        foreach (var entry in entries)
        {
            if (entry.IsFavorite == favorite) continue;
            entry.IsFavorite = favorite;
            favoriteService.SetFavorite(entry.FolderName, favorite);
            changed = true;
        }
        if (!changed) return;
        OnSelectionMenuChanged();

        if (_filter.FavoriteMode != FavoriteMode.None)
            Refresh(_filter);
        else
            RefreshCountsOnly();
    }

    /// <summary>The lock on a card: that livery alone</summary>
    [RelayCommand]
    private void ToggleLock(LiveryEntry entry) => SetLocks([entry], !entry.IsLocked);

    /// <summary>The context menu: every selected livery gets locked, or, when all of them already are, unlocked</summary>
    [RelayCommand]
    private void ToggleLocks(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry);
        if (targets.Count == 0) return;
        SetLocks(targets, targets.Any(e => !e.IsLocked));
    }

    private void SetLocks(IReadOnlyList<LiveryEntry> entries, bool locked)
    {
        bool changed = false;
        foreach (var entry in entries)
        {
            if (entry.IsLocked == locked) continue;
            entry.IsLocked = locked;
            lockService.SetLocked(entry.FolderName, locked);
            changed = true;
        }
        if (!changed) return;
        OnSelectionMenuChanged();
        LocksChanged?.Invoke();
    }

    [RelayCommand]
    private void ShowAuthorRow(LiveryEntry entry) => AuthorRowRequested?.Invoke(entry);

    /// <summary>The card's tag button or the context menu: the card, or every selected livery when it is one of them</summary>
    [RelayCommand]
    private void EditTags(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry);
        if (targets.Count > 0) EditTagsRequested?.Invoke(targets);
    }

    /// <summary>The liveries whose author the user may say: each selected one, or the card alone (never empty)</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? AssignAuthorRequested;

    /// <summary>The liveries whose author the user said, to forget it (never empty)</summary>
    public event Action<IReadOnlyList<LiveryEntry>>? UnassignAuthorRequested;

    private static bool CanAssign(LiveryEntry entry) => entry.CanAssignAuthor && entry.CLiveryHash is not null;

    /// <summary>
    /// The context menu or the card: every selected livery whose own id does not tell the author (one author for all of
    /// them), or the card alone. Liveries with an account's id are left out
    /// </summary>
    [RelayCommand]
    private void AssignAuthor(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry).Where(CanAssign).ToList();
        if (targets.Count > 0) AssignAuthorRequested?.Invoke(targets);
    }

    [RelayCommand]
    private void UnassignAuthor(LiveryEntry? entry)
    {
        var targets = TargetsOf(entry).Where(e => e.IsAuthorAssigned).ToList();
        if (targets.Count > 0) UnassignAuthorRequested?.Invoke(targets);
    }

    private int AssignableSelectedCount => SelectedEntries.Count(CanAssign);
    private int AssignedSelectedCount => SelectedEntries.Count(e => e.IsAuthorAssigned);

    /// <summary>Some selected livery's author can be said (the menu selects its card first)</summary>
    public bool AssignAuthorMenuVisible => AssignableSelectedCount > 0;

    public string AssignAuthorMenuHeader => WithCount(Strings.ContextMenuAssignAuthor, AssignableSelectedCount);

    /// <summary>Some selected livery has an author the user said</summary>
    public bool UnassignAuthorMenuVisible => AssignedSelectedCount > 0;

    public string UnassignAuthorMenuHeader => WithCount(Strings.ContextMenuUnassignAuthor, AssignedSelectedCount);

    /// <summary>The menu's author items after the user changed whose liveries they are</summary>
    public void RefreshAuthorMenu() => OnSelectionMenuChanged();

    public void RefreshDuplicateTooltips()
    {
        foreach (var entry in AllEntries) entry.RefreshDuplicateTooltip();
    }

    public void RefreshPossibleDuplicateThreshold()
    {
        foreach (var entry in AllEntries) entry.RefreshPossibleDuplicateThreshold();
    }

    [RelayCommand]
    private void ViewPreview(LiveryEntry entry) => ViewPreviewRequested?.Invoke(entry);

    [RelayCommand]
    private void View3D(LiveryEntry entry)
    {
        if (entry.CanView3D) View3DRequested?.Invoke(entry);
    }

    public void RefreshView3DAvailability()
    {
        foreach (var entry in _allEntries) entry.RefreshView3D();
    }

    public TagFilterState TagState(string tag) =>
        _selectedTags.Contains(tag) ? TagFilterState.Show
        : _excludedTags.Contains(tag) ? TagFilterState.Hide
        : TagFilterState.Off;

    /// <summary>Shows only the tag's liveries, hides them, or stops filtering by the tag</summary>
    public void SetTagState(string tag, TagFilterState state)
    {
        bool changed = state == TagFilterState.Show ? _selectedTags.Add(tag) : _selectedTags.Remove(tag);
        changed |= state == TagFilterState.Hide ? _excludedTags.Add(tag) : _excludedTags.Remove(tag);
        if (!changed) return;
        SelectedTagsChanged?.Invoke();
        Refresh(_filter);
    }

    public void SetTagSelected(string tag, bool selected) => SetTagState(tag, selected ? TagFilterState.Show : TagFilterState.Off);

    /// <summary>No tag shows or hides anything</summary>
    public void ClearTagFilter()
    {
        if (!HasTagFilter) return;
        _selectedTags.Clear();
        _excludedTags.Clear();
        SelectedTagsChanged?.Invoke();
        Refresh(_filter);
    }

    /// <summary>A tag on a card: shows only its liveries, or stops</summary>
    [RelayCommand]
    private void ToggleTag(string tag) => SetTagSelected(tag, TagState(tag) != TagFilterState.Show);

    /// <summary>
    /// Stops filtering by tags no livery has any more (the last livery with one lost it): the row has no chip left to
    /// turn them off. When one was dropped the gallery is shown again, and true is returned
    /// </summary>
    public bool SyncKnownTags(IReadOnlyCollection<string> knownTags)
    {
        bool dropped = _selectedTags.RemoveWhere(t => !knownTags.Contains(t)) > 0;
        dropped |= _excludedTags.RemoveWhere(t => !knownTags.Contains(t)) > 0;
        if (!dropped) return false;
        SelectedTagsChanged?.Invoke();
        Refresh(_filter);
        return true;
    }

    public void Refresh(GalleryFilterState filter)
    {
        _filter = filter;
        foreach (var entry in _allEntries)
            entry.ShowMineBadge = entry.IsMine;

        var filtered = GetFilteredEntries();
        var groups = GalleryGroupingService.Group(
            filtered, filter.SortMode, filter.FavoriteMode, filter.MineMode, filter.AuctionFilterMode,
            filter.GroupingEnabled, GroupWidth, filter.InstalledMode);
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
        ApplyInstalledCounts();
        ReconcileSelection();
    }

    private IReadOnlyDictionary<string, int>? _installedCounts;

    public bool IsInstalledDataKnown => _installedCounts is not null;

    public void SetInstalledLiveries(IReadOnlyDictionary<string, int>? installedCounts)
    {
        _installedCounts = installedCounts is null
            ? null
            : new Dictionary<string, int>(installedCounts, StringComparer.OrdinalIgnoreCase);
        ApplyInstalledCounts();
    }

    public event Action? InstalledCountsChanged;

    private void ApplyInstalledCounts()
    {
        foreach (var entry in _allEntries)
            entry.InstalledCarCount = _installedCounts is not null && _installedCounts.TryGetValue(entry.FolderName, out int n) ? n : 0;
        InstalledCountsChanged?.Invoke();
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
                newEntry.IsLocked = previous.IsLocked;
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
            && a.AuthorRaw == b.AuthorRaw
            && a.Data.IsAuthorInferred == b.Data.IsAuthorInferred
            && a.CarId == b.CarId
            && a.CarManufacturerRaw == b.CarManufacturerRaw
            && a.CarModelNameRaw == b.CarModelNameRaw
            && a.CarYear == b.CarYear
            && a.CarKnown == b.CarKnown
            && a.CreatedYear == b.CreatedYear
            && a.CreatedMonth == b.CreatedMonth
            && a.DownloadDate == b.DownloadDate
            && a.ThumbnailPath == b.ThumbnailPath
            && a.Data.ExternalPreviewPath == b.Data.ExternalPreviewPath
            && a.DuplicateStatus == b.DuplicateStatus
            && PossibleDuplicateOfEqual(a.PossibleDuplicateOf, b.PossibleDuplicateOf)
            && a.IsMine == b.IsMine
            && a.IsPossiblyGenerated == b.IsPossiblyGenerated
            && a.HasNoLayers == b.HasNoLayers
            && a.HasParseError == b.HasParseError
            && ParseIssuesEqual(a.Data.ParseIssues, b.Data.ParseIssues)
            && a.LiveryId == b.LiveryId
            && a.CLiveryHash == b.CLiveryHash
            && a.AssignedCardId == b.AssignedCardId
            && a.Data.ArtworkSketch == b.Data.ArtworkSketch
            && a.Data.AuthorIdentityTagHex == b.Data.AuthorIdentityTagHex;
    }

    private static bool ParseIssuesEqual(IReadOnlyList<LiveryParseIssue>? a, IReadOnlyList<LiveryParseIssue>? b)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        return a.SequenceEqual(b);
    }

    private static bool PossibleDuplicateOfEqual(IReadOnlyList<DuplicateRelation>? a, IReadOnlyList<DuplicateRelation>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || a.Count == 0) return b is null || b.Count == 0;
        if (b is null || a.Count != b.Count) return false;
        return a.ToHashSet().SetEquals(b);
    }
}
