using LiveryGallery.Localisation;

namespace LiveryGallery.Models;

/// <summary>
/// How the gallery names a <see cref="QuickFilter"/> (ForzaToolkit.Liveries keeps it language-independent): an unknown
/// car, manufacturer or author in the user's language
/// </summary>
internal static class QuickFilterDisplay
{
    extension(QuickFilter filter)
    {
        public string DisplayText => filter.Kind switch
        {
            QuickFilterKind.Car when !filter.CarKnown => string.Format(Strings.UnknownCarIdFormat, filter.CarId),
            QuickFilterKind.Manufacturer when filter.Value is null => Strings.UnknownManufacturer,
            QuickFilterKind.Author when filter.Value is null => Strings.UnknownAuthor,
            QuickFilterKind.Country when filter.Value is null => Strings.UnknownCountry,
            QuickFilterKind.CarType when filter.Value is null => Strings.UnknownCarType,
            _ => filter.DisplayValue
        };

        public string FullDisplayText => filter.Kind == QuickFilterKind.Car && filter.Manufacturer is { } manufacturer
            ? $"{manufacturer.DisplayText} {filter.DisplayText}"
            : filter.DisplayText;
    }
}
