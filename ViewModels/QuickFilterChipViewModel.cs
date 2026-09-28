using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.Models;

namespace LiveryGallery.ViewModels;

internal sealed partial class QuickFilterChipViewModel(QuickFilter filter) : ObservableObject
{
    public QuickFilter Filter { get; } = filter;

    public string Text => string.Format(Filter.Kind switch
    {
        QuickFilterKind.Car => Strings.QuickFilterCarFormat,
        QuickFilterKind.Manufacturer => Strings.QuickFilterManufacturerFormat,
        _ => Strings.QuickFilterAuthorFormat
    }, Filter.DisplayText);

    public void RefreshLocalizedText() => OnPropertyChanged(nameof(Text));
}
