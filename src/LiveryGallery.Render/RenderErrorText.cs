using ForzaToolkit.LiveryRender;
using LiveryGallery.Localisation;

namespace LiveryGallery.Render;

/// <summary>The renderer's errors in the user's language (the viewer window shows them)</summary>
internal static class RenderErrorText
{
    public static string Describe(RenderError error) => error.Code switch
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
