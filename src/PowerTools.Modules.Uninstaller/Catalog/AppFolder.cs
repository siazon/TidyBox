using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Routing;

namespace PowerTools.Modules.Uninstaller.Catalog;

/// <summary>Works out which folder an installed program lives in, even when its registry entry has no InstallLocation.</summary>
public static class AppFolder
{
    /// <returns>An existing folder, or null when it cannot be told.</returns>
    public static string? Resolve(InstalledApp app, IReadOnlyList<string>? programFilesRoots = null)
    {
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && TryExistingFolder(app.InstallLocation, out var registered))
        {
            return registered;
        }

        // Many entries leave InstallLocation empty but keep the uninstaller next to the program, under Program Files.
        var command = UninstallCommandParser.Parse(app.UninstallString);
        if (command is null || !Path.IsPathRooted(command.FileName)) return null;

        var roots = programFilesRoots ?? DefaultProgramFilesRoots();
        if (Path.GetDirectoryName(command.FileName) is { Length: > 0 } dir
            && TryExistingFolder(dir, out var beside)
            && roots.Any(root => IsStrictlyUnder(beside, root)))
        {
            return beside;
        }

        return null;
    }

    private static IReadOnlyList<string> DefaultProgramFilesRoots() => new[]
    {
        Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    }.Where(r => r.Length > 0).ToList();

    private static bool TryExistingFolder(string path, out string folder)
    {
        folder = "";
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(full)) return false;
            folder = full;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsStrictlyUnder(string path, string root)
        => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + "\\", StringComparison.OrdinalIgnoreCase);
}
