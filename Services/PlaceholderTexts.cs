using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Globalization;

namespace LiveryGallery.Services;

internal static class PlaceholderTexts
{
    public static bool IsNameMissing(LiveryCacheEntry entry) =>
        string.IsNullOrEmpty(entry.LiveryName)
        || (entry.TextVersion < LiveryCacheEntry.CurrentTextVersion && IsNoLiveryName(entry.LiveryName));

    public static bool IsAuthorMissing(LiveryCacheEntry entry) =>
        string.IsNullOrEmpty(entry.Author)
        || (entry.TextVersion < LiveryCacheEntry.CurrentTextVersion && IsUnknownAuthor(entry.Author));

    public static bool NeedsTextMigration(LiveryCacheEntry entry) =>
        entry.TextVersion < LiveryCacheEntry.CurrentTextVersion
        && (IsNoLiveryName(entry.LiveryName) || IsUnknownAuthor(entry.Author));

    private static readonly string[] LanguageCodes = ["en", "ru", "de", "fr", "es", "it", "pt", "ja", "ko", "zh-Hans", "zh-Hant"];
    private static readonly HashSet<string> _unknownAuthor = Collect(nameof(Strings.UnknownAuthor));
    private static readonly HashSet<string> _noLiveryName = Collect(nameof(Strings.LiveryNoName));

    public static bool IsUnknownAuthor(string? value) =>
        string.IsNullOrWhiteSpace(value) || _unknownAuthor.Contains(value.Trim());

    public static bool IsNoLiveryName(string? value) =>
        string.IsNullOrWhiteSpace(value) || _noLiveryName.Contains(value.Trim());

    private static HashSet<string> Collect(string resourceName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(CultureInfo culture)
        {
            try
            {
                if (Strings.ResourceManager.GetString(resourceName, culture) is { Length: > 0 } value)
                    values.Add(value.Trim());
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled($"{resourceName}|{culture.Name}", $"Failed to load '{resourceName}' for '{culture.Name}'", ex);
            }
        }

        Add(CultureInfo.InvariantCulture);
        foreach (string code in LanguageCodes) Add(CultureInfo.GetCultureInfo(code));
        return values;
    }
}
