using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Residue;

namespace PowerTools.Modules.Uninstaller.Icons;

/// <summary>Where to take an icon from: an icon in a file, or a Store package (File is null then).</summary>
public record IconTarget(string? File, int Index, string? PackageFullName);

public static class IconTargetResolver
{
    /// <returns>Null when nothing reliable is known: showing a placeholder beats showing another program's icon.</returns>
    public static IconTarget? Resolve(InstalledApp app)
    {
        if (app.Source == AppSource.Store)
        {
            return string.IsNullOrEmpty(app.PackageFullName) ? null : new IconTarget(null, 0, app.PackageFullName);
        }

        var registered = DisplayIconParser.Parse(app.DisplayIcon);
        if (registered is not null && File.Exists(registered.File)) return new IconTarget(registered.File, registered.Index, null);

        // No usable DisplayIcon: take the program's own exe, but only when its name says it is the main one.
        var name = ResidueScanner.Normalize(app.DisplayName);
        if (name.Length == 0 || AppFolder.Resolve(app) is not { } folder) return null;

        try
        {
            var main = Directory.EnumerateFiles(folder, "*.exe")
                .FirstOrDefault(exe => ResidueScanner.Normalize(Path.GetFileNameWithoutExtension(exe)) == name);
            return main is null ? null : new IconTarget(main, 0, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
