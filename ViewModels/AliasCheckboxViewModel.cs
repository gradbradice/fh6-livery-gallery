using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveryGallery.ViewModels;

internal sealed partial class AliasCheckboxViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}
