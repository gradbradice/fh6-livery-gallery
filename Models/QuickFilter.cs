using LiveryGallery.Enums;
using LiveryGallery.Localisation;
using LiveryGallery.ViewModels;

namespace LiveryGallery.Models;

internal sealed record QuickFilter
{
    public required QuickFilterKind Kind { get; init; }
    public int CarId { get; init; }
    public string? Value { get; init; }
    public bool CarKnown { get; init; } = true;
    public QuickFilter? Manufacturer { get; init; }
    public required string DisplayValue { get; init; }

    public string DisplayText => Kind switch
    {
        QuickFilterKind.Car when !CarKnown => string.Format(Strings.UnknownCarIdFormat, CarId),
        QuickFilterKind.Manufacturer when Value is null => Strings.UnknownManufacturer,
        QuickFilterKind.Author when Value is null => Strings.UnknownAuthor,
        _ => DisplayValue
    };

    public string FullDisplayText => Kind == QuickFilterKind.Car && Manufacturer is { } manufacturer
        ? $"{manufacturer.DisplayText} {DisplayText}"
        : DisplayText;

    public static bool SameManufacturer(QuickFilter? a, QuickFilter? b) =>
        a is not null && b is not null && string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);

    public static QuickFilter ForCar(LiveryEntry entry)
    {
        string display = entry.CarModelName;
        if (entry.CarYear is int year) display += $" ({year})";
        return new QuickFilter
        {
            Kind = QuickFilterKind.Car,
            CarId = entry.CarId,
            CarKnown = entry.CarKnown,
            Manufacturer = ForManufacturer(entry),
            DisplayValue = display,
        };
    }

    public static QuickFilter ForManufacturer(LiveryEntry entry) => new()
    {
        Kind = QuickFilterKind.Manufacturer,
        Value = entry.CarKnown ? entry.CarManufacturerRaw : null,
        DisplayValue = entry.CarManufacturer,
    };

    public static QuickFilter ForAuthor(LiveryEntry entry) => new()
    {
        Kind = QuickFilterKind.Author,
        Value = entry.Data.IsAuthorUnknown ? null : entry.Author,
        DisplayValue = entry.Author,
    };

    public bool Matches(LiveryEntry entry) => Kind switch
    {
        QuickFilterKind.Car => entry.CarId == CarId,
        QuickFilterKind.Manufacturer => Value is null
            ? !entry.CarKnown
            : entry.CarKnown && string.Equals(entry.CarManufacturerRaw, Value, StringComparison.OrdinalIgnoreCase),
        QuickFilterKind.Author => Value is null
            ? entry.Data.IsAuthorUnknown
            : !entry.Data.IsAuthorUnknown && string.Equals(entry.Author, Value, StringComparison.OrdinalIgnoreCase),
        _ => true
    };
}
