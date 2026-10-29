namespace TidyBox.Modules.Uninstaller.Catalog;

public static class FolderSizer
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // A junction or symlink would count somebody else's files (or loop forever).
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <returns>Total size of the files below the folder in bytes, or null when the folder does not exist.</returns>
    public static long? Measure(string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) return null;

        long total = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Options))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                total += file.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file that vanished or is locked just does not count.
            }
        }

        return total;
    }
}
