namespace TidyBox.Modules.DiskAnalyzer.Steam;

/// <param name="LibraryPath">The Steam library this folder is under.</param>
/// <param name="AppId">The numeric Steam app id the folder is named after.</param>
/// <param name="GameName">The game's name if it is still known (from the cache), otherwise null.</param>
/// <param name="FullPath">Full path to the folder.</param>
/// <param name="SizeBytes">Real size on disk, recursively measured.</param>
public record WorkshopResidueItem(string LibraryPath, string AppId, string? GameName, string FullPath, long SizeBytes);

/// <summary>
/// Finds steamapps/workshop/content/&lt;appid&gt; folders for a game that is no longer installed. Steam does not
/// clean these up on uninstall (a long-standing, well-known behavior). Read-only: never deletes anything, only
/// reports candidates for the caller to confirm, since some users intentionally keep subscribed content around.
/// </summary>
public sealed class WorkshopResidueScanner
{
    private readonly ISteamAppNameLookup _nameLookup;

    public WorkshopResidueScanner(ISteamAppNameLookup? nameLookup = null) => _nameLookup = nameLookup ?? new SteamAppCache();

    public IReadOnlyList<WorkshopResidueItem> Scan(IReadOnlyList<string> libraryFolders, IReadOnlyList<string> installedAppIds)
    {
        var installed = installedAppIds.ToHashSet(StringComparer.Ordinal);
        var results = new List<WorkshopResidueItem>();

        foreach (var library in libraryFolders.Select(FullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var contentDir = Path.Combine(library, "steamapps", "workshop", "content");
            if (!Directory.Exists(contentDir)) continue;

            foreach (var dir in SafeDirectories(contentDir))
            {
                var appId = Path.GetFileName(dir);
                if (installed.Contains(appId)) continue;

                results.Add(new WorkshopResidueItem(library, appId, _nameLookup.TryGetName(appId), dir, MeasureSize(dir)));
            }
        }

        return results.OrderByDescending(r => r.SizeBytes).ToList();
    }

    private static string FullPath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(d => !new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint))
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static long MeasureSize(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        long total = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            try
            {
                total += file.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone or locked between enumerate and read; just does not count.
            }
        }
        return total;
    }
}
