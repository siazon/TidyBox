namespace TidyBox.Modules.Downloads.Scanning;

/// <summary>Files TidyBox must not touch yet: still downloading, in use, just written, or not really a user's file. They appear neither in tidy proposals nor in the review list.</summary>
public static class SafetyFilter
{
    private static readonly string[] UnfinishedExtensions = { ".crdownload", ".part", ".tmp", ".partial", ".opdownload" };
    private static readonly string[] SystemFiles = { "desktop.ini", "Thumbs.db" };

    /// <param name="recentWindow">A file written more recently than this is probably still being downloaded or edited.</param>
    public static bool IsEligible(FileInfo file, DateTime nowUtc, TimeSpan recentWindow)
    {
        if (UnfinishedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)) return false;
        if (SystemFiles.Contains(file.Name, StringComparer.OrdinalIgnoreCase)) return false;
        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;

        try
        {
            if (file.Length == 0) return false;
            if (nowUtc - file.LastWriteTimeUtc < recentWindow) return false;

            // Another program holding the file open means it is in use (or still being written).
            using var probe = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked, or gone since the scan started: either way, not something to touch now.
            return false;
        }
    }
}
