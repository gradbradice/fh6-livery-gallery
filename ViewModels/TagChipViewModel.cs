using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class TagChipViewModel : ObservableObject
{
    private readonly Action<string, bool> _onToggled;

    public string Tag { get; }

    [ObservableProperty]
    private bool _isSelected;

    public TagChipViewModel(string tag, bool isSelected, Action<string, bool> onToggled)
    {
        Tag = tag;
        _isSelected = isSelected;
        _onToggled = onToggled;
    }

    partial void OnIsSelectedChanged(bool value) => _onToggled(Tag, value);
}
