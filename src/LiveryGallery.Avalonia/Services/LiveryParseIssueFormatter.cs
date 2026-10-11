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

    /// <summary>
    /// The name of a parse error code; the parser's package knows them (the host sets this). Without it the number.
    /// </summary>
    public static Func<int, string?>? CodeNames { get; set; }

    // 0 = the parser didn't report anything: an exception was thrown instead (details in the log).
    private static string CodeName(int code) =>
        code == 0 ? "Exception" : CodeNames?.Invoke(code) is { } name ? $"{name} ({code})" : code.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
