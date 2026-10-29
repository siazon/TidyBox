using System.Text.RegularExpressions;
using TidyBox.Modules.Uninstaller.Catalog;
using TidyBox.Modules.Uninstaller.Model;
using TidyBox.Modules.Uninstaller.Residue;

namespace TidyBox.Modules.Uninstaller.Icons;

/// <summary>Where to take an icon from: an icon in a file, or a Store package (File is null then).</summary>
public record IconTarget(string? File, int Index, string? PackageFullName);

public static class IconTargetResolver
{
    // Programs that are about installing or updating other things, not the app itself.
    private static readonly string[] NotTheMainProgram = { "unins", "uninstall", "update", "setup", "install" };

    private static readonly Regex Words = new(@"\p{L}{4,}", RegexOptions.Compiled);
    private static readonly Regex TrailingNumbers = new(@"[\d_\-\s]+$", RegexOptions.Compiled);

    /// <returns>Null when nothing reliable is known: showing a placeholder beats showing another program's icon.</returns>
    public static IconTarget? Resolve(InstalledApp app)
    {
        if (app.Source == AppSource.Store)
        {
            return string.IsNullOrEmpty(app.PackageFullName) ? null : new IconTarget(null, 0, app.PackageFullName);
        }

        var registered = DisplayIconParser.Parse(app.DisplayIcon);
        if (registered is not null && File.Exists(registered.File)) return new IconTarget(registered.File, registered.Index, null);

        if (AppFolder.Resolve(app) is not { } folder) return null;

        // No usable DisplayIcon: take the program's own exe, but only when its name clearly says it is the main one.
        var main = MainExe(folder, app.DisplayName);
        return main is null ? null : new IconTarget(main, 0, null);
    }

    private static string? MainExe(string folder, string displayName)
    {
        var fullName = ResidueScanner.Normalize(displayName);
        var words = Words.Matches(displayName).Select(m => m.Value.ToLowerInvariant()).Where(ResidueScanner.Eligible).ToHashSet();
        if (fullName.Length == 0 && words.Count == 0) return null;

        var exes = ExesIn(folder).Where(exe => !NotTheMainProgram.Any(bad => Path.GetFileName(exe).Contains(bad, StringComparison.OrdinalIgnoreCase))).ToList();

        // Best case: the exe is named exactly like the app ("Foo Studio" -> FooStudio.exe).
        var exact = exes.FirstOrDefault(exe => ResidueScanner.Normalize(Path.GetFileNameWithoutExtension(exe)) == fullName && fullName.Length > 0);
        if (exact is not null) return exact;

        // Otherwise the exe may carry just one word of the name ("Android Studio" -> studio64.exe); two different
        // words matching two different exes means we cannot tell which is the main one.
        var byWord = exes
            .GroupBy(exe => TrailingNumbers.Replace(Path.GetFileNameWithoutExtension(exe).ToLowerInvariant(), ""))
            .Where(group => words.Contains(group.Key))
            .ToList();
        if (byWord.Count != 1) return null;

        return byWord[0].OrderByDescending(exe => Path.GetFileNameWithoutExtension(exe).EndsWith("64", StringComparison.OrdinalIgnoreCase)).First();
    }

    private static IEnumerable<string> ExesIn(string folder)
    {
        foreach (var dir in new[] { folder, Path.Combine(folder, "bin") })
        {
            IEnumerable<string> found;
            try
            {
                found = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.exe").ToList() : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var exe in found) yield return exe;
        }
    }
}
