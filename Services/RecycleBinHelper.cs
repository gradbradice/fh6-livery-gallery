using System.Runtime.InteropServices;

namespace LiveryGallery.Services;

internal static class RecycleBinHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT fileOp);

    public static bool TrySendToRecycleBin(string path)
    {
        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        try
        {
            int result = SHFileOperationW(ref fileOp);
            return result == 0 && fileOp.fAnyOperationsAborted == 0;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to send '{path}' to the Recycle Bin", ex);
            return false;
        }
    }
}
