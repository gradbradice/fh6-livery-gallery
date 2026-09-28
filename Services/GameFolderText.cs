using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Assets;
using LiveryGallery.Localisation;

namespace LiveryGallery.Services;

internal static class GameFolderText
{
    public static string Describe(GameFolderProblem problem) => problem switch
    {
        GameFolderProblem.NotSet => Strings.GameFolderProblemNotSet,
        GameFolderProblem.NotFound => Strings.GameFolderProblemNotFound,
        GameFolderProblem.AccessDenied => Strings.GameFolderProblemAccessDenied,
        GameFolderProblem.NoMediaFolder => Strings.GameFolderProblemNoMediaFolder,
        GameFolderProblem.NoCarsFolder => Strings.GameFolderProblemNoCarsFolder,
        GameFolderProblem.NoCarArchives => Strings.GameFolderProblemNoCarArchives,
        GameFolderProblem.NoLiveryFolder => Strings.GameFolderProblemNoLiveryFolder,
        GameFolderProblem.NoVinyls => Strings.GameFolderProblemNoVinyls,
        GameFolderProblem.VinylsUnreadable => Strings.GameFolderProblemVinylsUnreadable,
        GameFolderProblem.NoDecals => Strings.GameFolderProblemNoDecals,
        GameFolderProblem.CarArchiveUnreadable => Strings.GameFolderProblemCarArchiveUnreadable,
        _ => Strings.GameFolderProblemUnknown
    };

    public static string? FirstError(GameFolderCheck? check) =>
        check?.Issues.FirstOrDefault(i => i.IsError) is { } error ? Describe(error.Code) : null;

    public static string Warnings(GameFolderCheck check) =>
        string.Join("; ", check.Issues.Where(i => !i.IsError).Select(i => i.Code).Distinct().Select(Describe));

    public static string RenderError(RenderError error) => error.Code switch
    {
        RenderErrorCode.LiveryFilesNotFound => Strings.RenderErrorLiveryFilesNotFound,
        RenderErrorCode.GamePathInvalid => Strings.RenderErrorGamePathInvalid,
        RenderErrorCode.CarNotFound => Strings.RenderErrorCarNotFound,
        RenderErrorCode.CarFormat => Strings.RenderErrorCarFormat,
        RenderErrorCode.NoLiveryLayout => Strings.RenderErrorNoLiveryLayout,
        RenderErrorCode.MissingShapes => string.Format(Strings.RenderErrorMissingShapesFormat, error.Count ?? 0),
        RenderErrorCode.RasterLogosSkipped => string.Format(Strings.RenderErrorRasterLogosSkippedFormat, error.Count ?? 0),
        RenderErrorCode.ShapesWithoutSection => string.Format(Strings.RenderErrorShapesWithoutSectionFormat, error.Count ?? 0),
        _ => Strings.RenderErrorGeneric
    };
}
