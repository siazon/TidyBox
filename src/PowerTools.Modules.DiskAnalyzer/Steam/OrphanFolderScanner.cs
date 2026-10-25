namespace PowerTools.Modules.DiskAnalyzer.Steam;

/// <param name="LibraryPath">The Steam library this folder is under.</param>
/// <param name="FolderName">The folder's own name, directly under steamapps/common.</param>
/// <param name="FullPath">Full path to the folder.</param>
/// <param name="SizeBytes">Real size on disk, recursively measured.</param>
/// <param name="LastWriteTimeUtc">The folder's own last-write time (not recursive); used as the "still changing" guard.</param>
public record OrphanGameFolder(string LibraryPath, string FolderName, string FullPath, long SizeBytes, DateTime LastWriteTimeUtc);

/// <summary>
/// Finds folders under steamapps/common that no appmanifest points to any more (deleted manifest, interrupted
/// uninstall/move). Read-only: never deletes anything, only reports candidates for the caller to confirm.
/// </summary>
public sealed class OrphanFolderScanner
{
    private readonly SteamLibraryParser _parser;
    private readonly IReadOnlySet<string> _knownSharedFolders;
    private readonly TimeSpan _recentChangeGuard;

    public OrphanFolderScanner(SteamLibraryParser? parser = null, IReadOnlySet<string>? knownSharedFolders = null, TimeSpan? recentChangeGuard = null)
    {
        _parser = parser ?? new SteamLibraryParser();
        _knownSharedFolders = knownSharedFolders ?? KnownSharedFolders.Load();
        _recentChangeGuard = recentChangeGuard ?? TimeSpan.FromMinutes(10);
    }

    public IReadOnlyList<OrphanGameFolder> Scan(IReadOnlyList<string> libraryFolders)
    {
        var libraries = libraryFolders.Select(FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var knownInstallPaths = libraries
            .SelectMany(lib => _parser.ParseLibrary(lib))
            .Select(g => g.InstallPath)
            .Where(p => p.Length > 0)
            .Select(FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var results = new List<OrphanGameFolder>();

        foreach (var library in libraries)
        {
            var commonDir = Path.Combine(library, "steamapps", "common");
            if (!Directory.Exists(commonDir)) continue;

            foreach (var dir in SafeDirectories(commonDir))
            {
                if (knownInstallPaths.Contains(dir)) continue;

                var name = Path.GetFileName(dir);
                if (_knownSharedFolders.Contains(KnownSharedFolders.Normalize(name))) continue;

                var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                if (now - lastWrite < _recentChangeGuard) continue; // may still be mid-download/update

                results.Add(new OrphanGameFolder(library, name, dir, MeasureSize(dir), lastWrite));
            }
        }

        return results.OrderByDescending(r => r.SizeBytes).ToList();
    }

    // Normalized so "C:\Lib" and "C:\Lib\" (or two library-list entries for the same path) are the same folder.
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
