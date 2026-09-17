namespace LiveryGallery.Enums;

/// <summary>
/// General purpose consistency status for a scanned livery.
/// Used for CarId (folder name, header, C_livery)
/// </summary>
internal enum LiveryConsistency
{
    /// <summary>All available sources agree (or fewer than two were available to compare)</summary>
    Consistent = 0,

    /// <summary>At least one available source disagreed with the others</summary>
    Mismatched = 1,
}
