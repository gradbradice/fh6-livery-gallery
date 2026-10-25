using ForzaToolkit.Formats;
using ForzaToolkit.LiveryRender;
using ForzaToolkit.LiveryRender.Avalonia;
using LiveryGallery.Services;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Render;

/// <summary>What the closed components tell the open gallery: their versions and the names of parse errors</summary>
internal static class RenderComponents
{
    /// <summary>The closed packages the app ships with and the native fdata library, by name</summary>
    public static AboutComponents ReadVersions() => new(
    [
        .. new[]
        {
            typeof(NativeHeaderParser),
            typeof(global::ForzaToolkit.Liveries.Formats.Fh6LiveryParser),
            typeof(LiveryRenderer),
            typeof(global::ForzaToolkit.LiveryRender.Formats.LiveryDrawings),
            typeof(LiveryViewer),
            typeof(global::ForzaToolkit.Crypto.Client.ForzaLiveryClient),
        }
        .Select(ComponentVersions.Of)
        .OrderBy(c => c.Name, StringComparer.Ordinal),
        new ComponentVersion("fdata (native)", SafeNativeVersion()),
    ]);

    /// <summary>For LiveryParseIssueFormatter.CodeNames: Formats' name of an error code</summary>
    public static string? ParseErrorName(int code) =>
        Enum.IsDefined(typeof(ParseErrorCode), code) ? ((ParseErrorCode)code).ToString() : null;

    private static string SafeNativeVersion()
    {
        try
        {
            return NativeHeaderParser.GetVersion();
        }
        catch (Exception ex)
        {
            AppLog.LogError("Failed to query the native fdata version", ex);
            return "?";
        }
    }
}
