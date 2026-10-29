namespace TidyBox.Modules.Downloads.Tidy;

internal static class PathGuard
{
    public static bool IsInside(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <exception cref="ArgumentException"><paramref name="path"/> is not inside <paramref name="root"/> (after resolving "..").</exception>
    public static void RequireInside(string root, string path)
    {
        if (!IsInside(root, path)) throw new ArgumentException($"'{path}' is not inside the downloads folder.", nameof(path));
    }
}
