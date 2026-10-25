using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Services;

namespace LiveryGallery.ViewModels;

/// <summary>One account in the card editor: its name as the gallery shows it, and how many liveries</summary>
internal sealed partial class AccountCheckboxViewModel(AuthorAccountChoice choice) : ObservableObject
{
    public string IdentityTag { get; } = choice.Option.IdentityTag;

    /// <summary>Its nicknames, oldest first</summary>
    public IReadOnlyList<string> Names { get; } = choice.Option.Names;

    public string Label { get; } = choice.Label;

    public string Details { get; } = choice.Details;

    /// <summary>True when the search text is in one of its nicknames or its label</summary>
    public bool Matches(string text) =>
        Label.Contains(text, StringComparison.OrdinalIgnoreCase) || Names.Any(n => n.Contains(text, StringComparison.OrdinalIgnoreCase));

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}
