using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Catalog;

public static class AppMatcher
{
    /// <summary>The installed program whose folder contains the given file or folder; the deepest folder wins when programs are nested.</summary>
    public static InstalledApp? Find(IEnumerable<InstalledApp> apps, string path, IReadOnlyList<string>? programFilesRoots = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;

        var target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        InstalledApp? best = null;
        var bestLength = -1;
        foreach (var app in apps)
        {
            var folder = AppFolder.Resolve(app, programFilesRoots);
            if (folder is null || folder.Length <= bestLength) continue;

            if (string.Equals(target, folder, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
            {
                best = app;
                bestLength = folder.Length;
            }
        }

        return best;
    }
}
