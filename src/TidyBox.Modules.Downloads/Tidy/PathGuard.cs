namespace TidyBox.Modules.Downloads.Tidy;

internal static class PathGuard
{
    /// <exception cref="ArgumentException"><paramref name="path"/> is not inside <paramref name="root"/> (after resolving "..").</exception>
    public static void RequireInside(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{path}' is not inside the downloads folder.", nameof(path));
        }
    }
}
