using LiveryGallery.Enums;

namespace LiveryGallery.Services;

internal static class CarIdResolver
{
    public static (int CarId, LiveryConsistency Consistency) Resolve(int folderCarId, uint? headerCarId, uint? cLiveryCarId)
    {
        int resolved = cLiveryCarId.HasValue ? (int)cLiveryCarId.Value
            : headerCarId.HasValue ? (int)headerCarId.Value
            : folderCarId;

        var available = new List<int> { folderCarId };
        if (headerCarId.HasValue) available.Add((int)headerCarId.Value);
        if (cLiveryCarId.HasValue) available.Add((int)cLiveryCarId.Value);

        var consistency = available.Distinct().Count() > 1 ? LiveryConsistency.Mismatched : LiveryConsistency.Consistent;
        return (resolved, consistency);
    }
}
