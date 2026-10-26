using Microsoft.Win32;
using PowerTools.Core.Modules;
using PowerTools.Core.Recycling;
using PowerTools.Modules.DiskAnalyzer.History;
using PowerTools.Modules.DiskAnalyzer.Scanning;
using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer;

public class DiskAnalyzerModule : IToolModule
{
    private readonly string? _steamInstallPathOverride;
    private readonly string _snapshotDirectory;
    private readonly ISteamUninstallLauncher _steamUninstallLauncher;
    private readonly SnapshotStore _snapshotStore;
    private readonly DirectoryScanner _scanner = new();
    private readonly SteamLibraryParser _steamParser = new();
    private readonly OrphanFolderScanner _orphanScanner = new();
    private readonly WorkshopResidueScanner _workshopScanner;
    private readonly ISteamAppNameLookup _steamAppCache;
    private readonly IRecycleBin _recycleBin;

    public DiskAnalyzerModule(
        string? steamInstallPathOverride = null,
        string? snapshotDirectory = null,
        ISteamUninstallLauncher? steamUninstallLauncher = null,
        IRecycleBin? recycleBin = null,
        ISteamAppNameLookup? steamAppCache = null)
    {
        _steamInstallPathOverride = steamInstallPathOverride;
        _snapshotDirectory = snapshotDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerTools", "DiskAnalyzer", "snapshots");
        _steamUninstallLauncher = steamUninstallLauncher ?? new SteamUninstallLauncher();
        _snapshotStore = new SnapshotStore(_snapshotDirectory);
        _recycleBin = recycleBin ?? new ShellRecycleBin();
        _steamAppCache = steamAppCache ?? new SteamAppCache();
        _workshopScanner = new WorkshopResidueScanner(_steamAppCache);
    }

    public string ModuleId => "disk-analyzer";

    public string DisplayName => "磁盘空间分析";

    /// <summary>False until <see cref="InitializeAsync"/> has run, and when Steam is not installed.</summary>
    public bool IsSteamAvailable => SteamInstallPath is not null;

    public string? SteamInstallPath { get; private set; }

    public Task InitializeAsync()
    {
        // Anything that can really fail belongs here so the shell can show the module as failed instead of
        // letting it limp along: without a writable snapshot folder, history comparison cannot work.
        try
        {
            Directory.CreateDirectory(_snapshotDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot create the disk analyzer snapshot folder '{_snapshotDirectory}'.", ex);
        }

        // A machine without Steam is a normal state, not an error: the Steam view just stays empty.
        var candidate = _steamInstallPathOverride ?? DetectSteamInstallPath();
        SteamInstallPath = candidate is not null && Directory.Exists(Path.Combine(candidate, "steamapps")) ? candidate : null;
        return Task.CompletedTask;
    }

    /// <summary>Scans the folder, stores the result as the new latest snapshot, and diffs it against the previous one.</summary>
    public async Task<DiskScanResult> ScanAsync(string rootPath, CancellationToken ct = default)
    {
        var previous = await _snapshotStore.LoadLatestAsync(rootPath);
        var root = await _scanner.ScanTreeAsync(rootPath, ct);
        var current = DirectoryScanner.ToSnapshot(root);
        await _snapshotStore.SaveAsync(current);

        var changes = previous is null ? Array.Empty<DiskScanEntryDelta>() : SnapshotDiff.Compare(previous, current);
        return new DiskScanResult(current, previous, changes, root);
    }

    /// <summary>Installed Steam games across all library folders, largest first; empty when Steam is not available.</summary>
    public IReadOnlyList<SteamGameInfo> GetSteamGames()
    {
        var games = GetLibraryFolders()
            .SelectMany(_steamParser.ParseLibrary)
            .GroupBy(game => game.AppId)
            .Select(group => group.First())
            .OrderByDescending(game => game.SizeBytes)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Best-effort: remembers each game's name so it can still be shown later for leftover workshop content
        // after the game itself is uninstalled. A cache write failure must never break the games list.
        if (_steamAppCache is SteamAppCache cache)
        {
            foreach (var game in games)
            {
                try
                {
                    cache.Upsert(game.AppId, game.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return games;
    }

    public void UninstallSteamGame(string appId) => _steamUninstallLauncher.LaunchUninstall(appId);

    /// <summary>
    /// steamapps/workshop/content/&lt;appid&gt; folders for a game that is no longer installed; largest first.
    /// Only reports candidates, never deletes anything. Empty when Steam is not available.
    /// </summary>
    public IReadOnlyList<WorkshopResidueItem> GetWorkshopResidues()
    {
        var installedAppIds = GetSteamGames().Select(g => g.AppId).ToList();
        return _workshopScanner.Scan(GetLibraryFolders(), installedAppIds);
    }

    /// <summary>
    /// Moves a workshop residue folder to the Recycle Bin. Re-checks that the folder is really under one of the
    /// current Steam libraries' steamapps/workshop/content before touching it.
    /// </summary>
    public Task RemoveWorkshopResidueAsync(WorkshopResidueItem item, CancellationToken ct = default)
    {
        var allowedRoots = GetLibraryFolders().Select(lib => Path.Combine(lib, "steamapps", "workshop", "content"));
        if (!allowedRoots.Any(root => IsDirectChildOf(item.FullPath, root)))
        {
            throw new ArgumentException($"'{item.FullPath}' is not under a known Steam library's steamapps/workshop/content.", nameof(item));
        }

        return Task.Run(() => _recycleBin.Delete(item.FullPath), ct);
    }

    /// <summary>
    /// Folders under steamapps/common that no appmanifest points to any more; largest first. Only reports
    /// candidates, never deletes anything. Empty when Steam is not available.
    /// </summary>
    public IReadOnlyList<OrphanGameFolder> GetOrphanFolders() => _orphanScanner.Scan(GetLibraryFolders());

    /// <summary>
    /// Moves an orphan folder to the Recycle Bin. Re-checks that the folder is really under one of the current
    /// Steam libraries' steamapps/common before touching it, in case a stale or tampered item is passed back in.
    /// </summary>
    public Task RemoveOrphanFolderAsync(OrphanGameFolder folder, CancellationToken ct = default)
    {
        var allowedRoots = GetLibraryFolders().Select(lib => Path.Combine(lib, "steamapps", "common"));
        if (!allowedRoots.Any(root => IsDirectChildOf(folder.FullPath, root)))
        {
            throw new ArgumentException($"'{folder.FullPath}' is not under a known Steam library's steamapps/common.", nameof(folder));
        }

        return Task.Run(() => _recycleBin.Delete(folder.FullPath), ct);
    }

    // Orphan folders are always a direct child of steamapps/common; a tampered request pointing anywhere else is rejected.
    private static bool IsDirectChildOf(string folderPath, string root)
        => string.Equals(Path.GetDirectoryName(Path.GetFullPath(folderPath)), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> GetLibraryFolders()
    {
        if (SteamInstallPath is null) return Array.Empty<string>();

        var libraries = _steamParser.FindLibraryFolders(SteamInstallPath);
        if (libraries.Count == 0) libraries = new[] { SteamInstallPath };

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? DetectSteamInstallPath()
    {
        // Steam records its install folder (with forward slashes) here; fall back to the default location.
        var fromRegistry = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(fromRegistry)) return fromRegistry.Replace('/', '\\');

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }
}
