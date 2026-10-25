using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Routing;

namespace PowerTools.Modules.Uninstaller.Catalog;

/// <summary>Spots entries Windows would fail on: the registered uninstaller or install folder is gone.</summary>
public static class AppHealth
{
    /// <returns>A short reason in Chinese, or null when the entry looks fine.</returns>
    public static string? Check(InstalledApp app)
    {
        // Steam and the package manager do their own bookkeeping.
        if (app.Source != AppSource.Win32) return null;

        var command = UninstallCommandParser.Parse(app.UninstallString);
        if (command is null) return "没有登记卸载命令";
        if (!ExecutableExists(command.FileName)) return "卸载程序已丢失";
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && !Directory.Exists(app.InstallLocation.Trim().Trim('"'))) return "安装目录已不存在";

        return null;
    }

    private static bool ExecutableExists(string fileName)
    {
        if (Path.IsPathRooted(fileName)) return File.Exists(fileName);

        // "MsiExec.exe", "rundll32": Windows finds these itself, in the system folders and on PATH.
        var folders = new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate) || File.Exists(candidate + ".exe")) return true;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        return false;
    }
}
