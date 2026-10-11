namespace LiveryGallery.ViewModels;

internal static class LiveryEntryAuthorExtensions
{
    /// <summary>The authors of the entries, for the author cards; entries whose author is unknown are left out</summary>
    public static IEnumerable<AuthorObservation> AuthorObservations(this IEnumerable<LiveryEntry> entries) =>
        entries
            .Where(e => !e.Data.IsAuthorUnknown)
            .Select(e => new AuthorObservation(e.AuthorRaw, e.AuthorIdentityTagHex, e.DownloadDate));
}
