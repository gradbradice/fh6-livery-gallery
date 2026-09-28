using LiveryGallery.Models;
using System.Security.Cryptography;
using System.Text;

namespace LiveryGallery.Services;

internal sealed class AuthorNameInference
{
    private readonly Dictionary<string, string> _nameByTag;

    public static AuthorNameInference Empty { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private AuthorNameInference(Dictionary<string, string> nameByTag) => _nameByTag = nameByTag;

    public static bool IsAuthorMissing(LiveryCacheEntry entry) => PlaceholderTexts.IsAuthorMissing(entry);

    public static AuthorNameInference Build(IEnumerable<LiveryCacheEntry> entries)
    {
        var latest = new Dictionary<string, (string Name, DateTime Date)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.AuthorIdentityTagHex is not { } tag || IsAuthorMissing(entry)) continue;
            var date = entry.DownloadDate ?? DateTime.MinValue;
            if (!latest.TryGetValue(tag, out var current) || date > current.Date)
                latest[tag] = (entry.Author, date);
        }

        var nameByTag = new Dictionary<string, string>(latest.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, value) in latest) nameByTag[tag] = value.Name;
        return new AuthorNameInference(nameByTag);
    }

    public (string Name, bool IsInferred, bool IsUnknown) Resolve(LiveryCacheEntry entry, Func<string, string?>? cardLookup = null)
    {
        if (!IsAuthorMissing(entry)) return (entry.Author, false, false);
        if (entry.AuthorIdentityTagHex is { } tag)
        {
            if (_nameByTag.TryGetValue(tag, out var known)) return (known, true, false);
            if (cardLookup?.Invoke(tag) is { Length: > 0 } fromCard) return (fromCard, true, false);
        }
        return (string.Empty, false, true);
    }

    public string Fingerprint(IEnumerable<LiveryCacheEntry> entries)
    {
        var lines = entries
            .Where(IsAuthorMissing)
            .Select(e => $"{e.FolderName}={Resolve(e).Name}")
            .OrderBy(s => s, StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }
}
