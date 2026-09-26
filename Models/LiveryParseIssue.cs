using Forza.Data;

namespace LiveryGallery.Models;

internal enum LiveryParseFile
{
    Header,
    CLivery
}

internal enum LiveryParseSeverity
{
    /// <summary>The file (or its main data) could not be read.</summary>
    Error,

    /// <summary>The main data was read; a part of the file (or an analysis of it) failed.</summary>
    Partial
}

internal sealed record LiveryParseIssue
{
    public required LiveryParseFile File { get; init; }
    public required LiveryParseSeverity Severity { get; init; }
    public required ParseErrorCode Code { get; init; }
    public required ParseLevel Level { get; init; }
    public long? ByteOffset { get; init; }
    public int? SectionIndex { get; init; }
    public long? Expected { get; init; }
    public long? Actual { get; init; }

    public static LiveryParseIssue From(LiveryParseFile file, LiveryParseSeverity severity, ParseError error) => new()
    {
        File = file,
        Severity = severity,
        Code = error.Code,
        Level = error.Level,
        ByteOffset = error.ByteOffset,
        SectionIndex = error.SectionIndex,
        Expected = error.Expected,
        Actual = error.Actual,
    };

    public static LiveryParseIssue Unexpected(LiveryParseFile file) => new()
    {
        File = file,
        Severity = LiveryParseSeverity.Error,
        Code = ParseErrorCode.None,
        Level = ParseLevel.None,
    };
}
