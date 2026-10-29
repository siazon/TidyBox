using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Catalog;

/// <summary>How much disk space an installed program takes, measured from its folders.</summary>
public static class AppSizer
{
    private static readonly string DefaultDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");

    /// <summary>
    /// The size of the program's folder. A Store app also keeps its data under %LOCALAPPDATA%\Packages, and the system's
    /// own Settings page counts both, so both are added.
    /// </summary>
    /// <param name="packageDataRoot">Where Store apps keep their data; defaults to %LOCALAPPDATA%\Packages.</param>
    /// <returns>Null when nothing could be measured.</returns>
    public static long? Measure(InstalledApp app, CancellationToken ct, string? packageDataRoot = null)
    {
        var install = AppFolder.Resolve(app) is { } folder ? FolderSizer.Measure(folder, ct) : null;
        if (app.Source != AppSource.Store) return install;

        var data = FamilyName(app.PackageFullName) is { } family
            ? FolderSizer.Measure(Path.Combine(packageDataRoot ?? DefaultDataRoot, family), ct)
            : null;

        // A folder the user may not read adds up to 0 bytes; that means "unknown", and must not show as a tiny app.
        var total = (install ?? 0) + (data ?? 0);
        return total == 0 ? null : total;
    }

    /// <summary>Why a size could not be worked out, in words for the user.</summary>
    public static string UnknownReason(InstalledApp app)
    {
        if (app.Source == AppSource.Store) return "读不到这个应用的文件夹，无法统计大小。";

        // Zero Install keeps programs in its own cache and registers no install location.
        return app.UninstallString?.Contains("0install", StringComparison.OrdinalIgnoreCase) == true
            ? "此程序由 Zero Install 安装，它的文件放在 Zero Install 自己的缓存里，安装位置没有登记，所以无法统计大小。"
            : "这个程序没有登记安装位置，无法统计大小。";
    }

    /// <summary>"Name_Version_Architecture_ResourceId_PublisherId" is the full name; the data folder is "Name_PublisherId".</summary>
    private static string? FamilyName(string? packageFullName)
    {
        var parts = packageFullName?.Split('_');
        return parts is { Length: 5 } && parts[0].Length > 0 && parts[4].Length > 0 ? $"{parts[0]}_{parts[4]}" : null;
    }
}
