using LiveryGallery.Enums;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services;

internal static class CarIdResolver
{
    private static readonly Regex FolderNameCarIdRegex = new(@"Livery_(?<carId>\d+)_", RegexOptions.Compiled);

    public static int TryParseCarIdFromFolderName(string folderName)
    {
        var m = FolderNameCarIdRegex.Match(folderName);
        return m.Success && int.TryParse(m.Groups["carId"].Value, out int carId) ? carId : 0;
    }

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
