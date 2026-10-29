using System.Runtime.InteropServices;

namespace TidyBox.Modules.Downloads;

public static class DownloadsFolder
{
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>The user's Downloads folder, wherever they have moved it; falls back to %USERPROFILE%\Downloads.</summary>
    public static string Resolve()
    {
        var hr = SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero, out var pointer);
        try
        {
            if (hr == 0 && Marshal.PtrToStringUni(pointer) is { Length: > 0 } path) return path;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer); // owned by the caller whether or not the call succeeded
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
