namespace TidyBox.Modules.Downloads.Scanning;

public sealed class DownloadScanner
{
    /// <summary>
    /// The eligible files directly in the downloads folder, plus those one level inside the category folders TidyBox itself
    /// writes to. Other folders (the user's own, other tools') are not entered, and neither is a category folder that is a link.
    /// </summary>
    public IReadOnlyList<DownloadFile> Scan(string downloadsRoot, IReadOnlyCollection<string> categoryFolderNames, DateTime nowUtc)
    {
        var root = new DirectoryInfo(downloadsRoot);
        if (!root.Exists) return Array.Empty<DownloadFile>();

        var files = new List<DownloadFile>();
        AddFiles(root, categoryFolder: null);

        foreach (var directory in root.EnumerateDirectories())
        {
            var isCategoryFolder = categoryFolderNames.Contains(directory.Name, StringComparer.OrdinalIgnoreCase);
            if (isCategoryFolder && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) AddFiles(directory, directory.Name);
        }

        return files;

        void AddFiles(DirectoryInfo directory, string? categoryFolder)
        {
            foreach (var file in directory.EnumerateFiles())
            {
                if (SafetyFilter.IsEligible(file, nowUtc))
                {
                    files.Add(new DownloadFile(file.FullName, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc, categoryFolder));
                }
            }
        }
    }
}
