using CommunityToolkit.Mvvm.ComponentModel;
using LiveryGallery.Localisation;

namespace LiveryGallery.ViewModels;

internal sealed partial class QuickFilterChipViewModel(QuickFilter filter) : ObservableObject
{
    public QuickFilter Filter { get; } = filter;

    public string Text => string.Format(Filter.Kind switch
    {
        QuickFilterKind.Car => Strings.QuickFilterCarFormat,
        QuickFilterKind.Manufacturer => Strings.QuickFilterManufacturerFormat,
        QuickFilterKind.Country => Strings.QuickFilterCountryFormat,
        QuickFilterKind.CarType => Strings.QuickFilterCarTypeFormat,
        QuickFilterKind.Selection => "{0}",
        _ => Strings.QuickFilterAuthorFormat
    }, Filter.DisplayText);

    public void RefreshLocalizedText() => OnPropertyChanged(nameof(Text));
}
