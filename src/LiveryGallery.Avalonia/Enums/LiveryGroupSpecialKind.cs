namespace LiveryGallery.Enums;

internal enum LiveryGroupSpecialKind
{
    None,
    DownloadMonth,
    UnknownDownloadDate,
    UnknownManufacturer,
    UnknownAuthor,
    AllLiveries,
    UnknownCountry,
    UnknownCarType,
    /// <summary>Liveries that are not in the in-game My Designs grid (auction ones, or position unknown)</summary>
    NotInMyDesigns
}
