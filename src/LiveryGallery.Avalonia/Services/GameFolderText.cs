using ForzaToolkit.Games.Installation;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

/// <summary>A game folder check's findings in the user's language</summary>
internal static class GameFolderText
{
    public static string Describe(GameFolderIssueCode code) => code switch
    {
        GameFolderIssueCode.NotSet => Strings.GameFolderProblemNotSet,
        GameFolderIssueCode.NotFound => Strings.GameFolderProblemNotFound,
        GameFolderIssueCode.AccessDenied => Strings.GameFolderProblemAccessDenied,
        GameFolderIssueCode.NoMediaFolder => Strings.GameFolderProblemNoMediaFolder,
        GameFolderIssueCode.NoCarsFolder => Strings.GameFolderProblemNoCarsFolder,
        GameFolderIssueCode.NoCarArchives => Strings.GameFolderProblemNoCarArchives,
        GameFolderIssueCode.NoLiveryFolder => Strings.GameFolderProblemNoLiveryFolder,
        GameFolderIssueCode.NoVinyls => Strings.GameFolderProblemNoVinyls,
        GameFolderIssueCode.VinylsUnreadable => Strings.GameFolderProblemVinylsUnreadable,
        GameFolderIssueCode.NoDecals => Strings.GameFolderProblemNoDecals,
        GameFolderIssueCode.CarArchiveUnreadable => Strings.GameFolderProblemCarArchiveUnreadable,
        GameFolderIssueCode.NoPaintMaterials => Strings.GameFolderProblemNoPaintMaterials,
        GameFolderIssueCode.NoPaintTextures => Strings.GameFolderProblemNoPaintTextures,
        GameFolderIssueCode.NoStringTables => Strings.GameFolderProblemNoStringTables,
        _ => Strings.GameFolderProblemUnknown
    };

    public static string? FirstError(GameFolderReport? report) =>
        report?.Findings.FirstOrDefault(f => f.IsError) is { } error ? Describe(error.Code) : null;

    public static string Warnings(GameFolderReport report) =>
        string.Join("; ", report.Findings.Where(f => !f.IsError).Select(f => f.Code).Distinct().Select(Describe));
}
