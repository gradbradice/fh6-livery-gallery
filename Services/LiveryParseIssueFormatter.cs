using ForzaToolkit.Formats;
using LiveryGallery.Localisation;
using LiveryGallery.Models;
using System.Text;

namespace LiveryGallery.Services;

internal static class LiveryParseIssueFormatter
{
    public static string BuildTooltip(IReadOnlyList<LiveryParseIssue>? issues, bool hasError)
    {
        var text = new StringBuilder(hasError ? Strings.ParseErrorBadgeTooltip : Strings.ParsePartialBadgeTooltip);
        if (issues is not { Count: > 0 }) return text.ToString();

        text.AppendLine();
        AppendIssueLines(text, issues);
        return text.ToString();
    }

    public static void AppendIssueLines(StringBuilder text, IEnumerable<LiveryParseIssue> issues)
    {
        foreach (var issue in issues)
        {
            string file = issue.File == LiveryParseFile.Header ? "header" : "C_livery";
            text.AppendLine();
            text.Append("• ").Append(file).Append(": ").Append(CodeName(issue.Code));
        }
    }

    // None = the parser didn't report anything: an exception was thrown instead (details in the log).
    private static string CodeName(ParseErrorCode code) =>
        code == ParseErrorCode.None ? "Exception" : $"{code} ({(int)code})";
}
