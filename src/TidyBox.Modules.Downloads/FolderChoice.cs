namespace TidyBox.Modules.Downloads;

/// <summary>Which folders the user may point the tidy page at. Anything that is not a place for the user's own files is refused.</summary>
public static class FolderChoice
{
    /// <returns>Null when the folder is fine; otherwise a sentence for the user saying why not.</returns>
    public static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return "请选择一个完整的文件夹路径。";

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full)) return "这个文件夹不存在。";

        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty), full, StringComparison.OrdinalIgnoreCase))
        {
            return "不能整理整个磁盘，请选择里面的某个文件夹。";
        }

        // TidyBox moves files out of the top level of the chosen folder, so a place that belongs to Windows or to installed programs must never be one.
        var protectedFolders = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
        };
        foreach (var folder in protectedFolders)
        {
            var protectedPath = Environment.GetFolderPath(folder);
            if (protectedPath.Length > 0 && IsSameOrInside(protectedPath, full)) return "这是系统或程序所在的文件夹，不能整理。";
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0 && string.Equals(Path.TrimEndingDirectorySeparator(profile), full, StringComparison.OrdinalIgnoreCase))
        {
            return "不能整理用户主目录本身，请选择里面的某个文件夹。";
        }

        return null;
    }

    private static bool IsSameOrInside(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
