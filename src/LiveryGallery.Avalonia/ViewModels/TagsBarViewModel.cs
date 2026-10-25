using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

/// <summary>The row of every tag in the gallery, each shown, hidden or off</summary>
internal sealed partial class TagsBarViewModel : ObservableObject
{
    private readonly Func<string, TagFilterState> _stateOf;
    private readonly Action<string, TagFilterState> _onStateChanged;
    private HashSet<string>? _lastAllTags;

    public ObservableCollection<TagChipViewModel> AllTags { get; } = [];

    [ObservableProperty]
    private bool _hasTags;

    public TagsBarViewModel(Func<string, TagFilterState> stateOf, Action<string, TagFilterState> onStateChanged)
    {
        _stateOf = stateOf;
        _onStateChanged = onStateChanged;
    }

    public bool RecomputeAllTags(IEnumerable<LiveryEntry> allEntries)
    {
        var allTagsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allEntries)
            foreach (var tag in entry.Tags)
                allTagsSet.Add(tag);

        if (_lastAllTags is not null && _lastAllTags.SetEquals(allTagsSet)) return false;
        _lastAllTags = allTagsSet;

        AllTags.Clear();
        foreach (var tag in allTagsSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            AllTags.Add(new TagChipViewModel(tag, _stateOf(tag), _onStateChanged));

        HasTags = AllTags.Count > 0;
        return true;
    }

    public void SyncSelection()
    {
        foreach (var chip in AllTags)
            chip.State = _stateOf(chip.Tag);
    }
}
