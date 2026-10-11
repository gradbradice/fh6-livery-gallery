using LiveryGallery.Localisation;
using LiveryGallery.Services;

namespace LiveryGallery.Models;

/// <summary>
/// How the gallery shows a livery's raw data (ForzaToolkit.Liveries keeps raw values only): an empty name and an
/// unknown author in the user's language, also for stand-ins an older version stored as text
/// </summary>
internal static class LiveryDataDisplay
{
    extension(LiveryData data)
    {
        public string DisplayLiveryName =>
            string.IsNullOrEmpty(data.LiveryName) || (data.HasLegacyText && PlaceholderTexts.IsNoLiveryName(data.LiveryName))
                ? Strings.LiveryNoName
                : data.LiveryName;

        public string DisplayAuthorRaw =>
            data.IsAuthorUnknown || string.IsNullOrEmpty(data.AuthorRaw) || (data.HasLegacyText && PlaceholderTexts.IsUnknownAuthor(data.AuthorRaw))
                ? Strings.UnknownAuthor
                : data.AuthorRaw;
    }
}
