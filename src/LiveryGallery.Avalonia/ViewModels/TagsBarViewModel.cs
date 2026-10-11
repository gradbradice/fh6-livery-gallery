using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class TagsBarViewModel : ObservableObject
{
    private readonly IReadOnlySet<string> _selectedTags;
    private readonly Action<string, bool> _onTagToggled;
    private HashSet<string>? _lastAllTags;

    public ObservableCollection<TagChipViewModel> AllTags { get; } = [];

    [ObservableProperty]
    private bool _hasTags;

    public TagsBarViewModel(IReadOnlySet<string> selectedTags, Action<string, bool> onTagToggled)
    {
        _selectedTags = selectedTags;
        _onTagToggled = onTagToggled;
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
            AllTags.Add(new TagChipViewModel(tag, _selectedTags.Contains(tag), _onTagToggled));

        HasTags = AllTags.Count > 0;
        return true;
    }

    public void SyncSelection()
    {
        foreach (var chip in AllTags)
            chip.IsSelected = _selectedTags.Contains(chip.Tag);
    }
}
