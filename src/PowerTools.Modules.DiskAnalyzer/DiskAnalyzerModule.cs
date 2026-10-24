using Microsoft.Win32;
using PowerTools.Core.Modules;
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

    public DiskAnalyzerModule(
        string? steamInstallPathOverride = null,
        string? snapshotDirectory = null,
        ISteamUninstallLauncher? steamUninstallLauncher = null)
    {
        _steamInstallPathOverride = steamInstallPathOverride;
        _snapshotDirectory = snapshotDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerTools", "DiskAnalyzer", "snapshots");
        _steamUninstallLauncher = steamUninstallLauncher ?? new SteamUninstallLauncher();
        _snapshotStore = new SnapshotStore(_snapshotDirectory);
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
        if (SteamInstallPath is null) return Array.Empty<SteamGameInfo>();

        var libraries = _steamParser.FindLibraryFolders(SteamInstallPath);
        if (libraries.Count == 0) libraries = new[] { SteamInstallPath };

        return libraries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(_steamParser.ParseLibrary)
            .GroupBy(game => game.AppId)
            .Select(group => group.First())
            .OrderByDescending(game => game.SizeBytes)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void UninstallSteamGame(string appId) => _steamUninstallLauncher.LaunchUninstall(appId);

    private static string? DetectSteamInstallPath()
    {
        // Steam records its install folder (with forward slashes) here; fall back to the default location.
        var fromRegistry = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(fromRegistry)) return fromRegistry.Replace('/', '\\');

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }
}
