using System.Text;
using System.Text.RegularExpressions;

namespace LiveryGallery.Services;

internal static class ChangelogService
{
    private static readonly Regex _languageHeader = new(@"^##(?!#)\s*(.+?)\s*$", RegexOptions.Compiled);

    public static string? Extract(string? body, string header, string? fallbackHeader = null)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        string? section = ExtractExact(body, header);
        if (section is not null) return section;

        if (fallbackHeader is not null && !fallbackHeader.Equals(header, StringComparison.OrdinalIgnoreCase))
            return ExtractExact(body, fallbackHeader);

        return null;
    }

    private static string? ExtractExact(string body, string header)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');

        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var m = _languageHeader.Match(lines[i]);
            if (m.Success && m.Groups[1].Value.Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }
        if (start == -1) return null;

        var sb = new StringBuilder();
        for (int i = start; i < lines.Length; i++)
        {
            if (_languageHeader.IsMatch(lines[i])) break;
            sb.AppendLine(lines[i]);
        }

        string result = sb.ToString().Trim();
        return result.Length > 0 ? result : null;
    }
}
