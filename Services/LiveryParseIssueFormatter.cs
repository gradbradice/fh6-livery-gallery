using Forza.Data;
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
        foreach (var issue in issues)
        {
            // File names are the literal names in the livery folder; codes are the parser's enum names.
            string file = issue.File == LiveryParseFile.Header ? "header" : "C_livery";
            text.AppendLine();
            text.Append("• ").Append(file).Append(": ").Append(CodeName(issue.Code));
        }
        return text.ToString();
    }

    // None = the parser didn't report anything: an exception was thrown instead (details in the log).
    private static string CodeName(ParseErrorCode code) =>
        code == ParseErrorCode.None ? "Exception" : $"{code} ({(int)code})";
}
