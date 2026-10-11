using System.Diagnostics;

namespace LiveryGallery.Services.Diagnostics;

internal static class ExplorerLauncher
{
    /// <summary>Opens Explorer with the file selected. Windows file names cannot contain quotes, so quoting is safe</summary>
    public static void ShowInFolder(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            AppLogger.LogErrorThrottled("ExplorerLauncher", $"Failed to show '{filePath}' in Explorer", ex);
        }
    }
}
