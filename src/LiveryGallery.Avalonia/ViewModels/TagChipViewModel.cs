using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LiveryGallery.ViewModels;

/// <summary>What a tag does to the gallery</summary>
internal enum TagFilterState
{
    /// <summary>Nothing</summary>
    Off,

    /// <summary>Only liveries with the tag are shown</summary>
    Show,

    /// <summary>Liveries with the tag are hidden</summary>
    Hide,
}

/// <summary>A tag in the row of tags: a click shows only its liveries, a right click hides them</summary>
internal sealed partial class TagChipViewModel : ObservableObject
{
    private readonly Action<string, TagFilterState> _onChanged;

    public string Tag { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(IsHidden))]
    private TagFilterState _state;

    public bool IsShown => State == TagFilterState.Show;
    public bool IsHidden => State == TagFilterState.Hide;

    public TagChipViewModel(string tag, TagFilterState state, Action<string, TagFilterState> onChanged)
    {
        Tag = tag;
        _state = state;
        _onChanged = onChanged;
    }

    /// <summary>The click: shows only the tag's liveries, or stops filtering by it</summary>
    [RelayCommand]
    private void ToggleShow() => Change(State == TagFilterState.Off ? TagFilterState.Show : TagFilterState.Off);

    /// <summary>The right click: hides the tag's liveries, or stops hiding them</summary>
    [RelayCommand]
    private void ToggleHide() => Change(State == TagFilterState.Hide ? TagFilterState.Off : TagFilterState.Hide);

    private void Change(TagFilterState state)
    {
        State = state;
        _onChanged(Tag, state);
    }
}
