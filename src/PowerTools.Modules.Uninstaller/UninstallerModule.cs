using PowerTools.Core.Elevation;
using PowerTools.Core.Modules;
using PowerTools.Core.Recycling;
using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Icons;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Residue;
using PowerTools.Modules.Uninstaller.Routing;

namespace PowerTools.Modules.Uninstaller;

/// <param name="Outcome">How the uninstall itself went.</param>
/// <param name="Leftovers">What is still on disk (or in the registry) afterwards; empty unless a desktop program uninstalled cleanly.</param>
/// <param name="DeferredScan">Set when the uninstaller handed over to a background process, so the scan could not run yet; call it to wait (within limits) for the uninstall to really finish and scan then.</param>
public record UninstallResult(UninstallOutcome Outcome, IReadOnlyList<ResidueItem> Leftovers, Func<CancellationToken, Task<IReadOnlyList<ResidueItem>>>? DeferredScan = null);

public sealed class UninstallerModule : IToolModule
{
    private readonly IReadOnlyList<IAppCatalog> _catalogs;
    private readonly UninstallRouter _router;
    private readonly ResidueScanner _scanner;
    private readonly ResidueRemover _remover;
    private readonly IAppIconProvider _icons;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly WatchOptions _watchOptions;
    private readonly IRecycleBin _recycleBin;
    private readonly IElevationClient _elevation;

    public UninstallerModule()
        : this(
            new IAppCatalog[] { Win32AppCatalog.Real(), new StoreAppCatalog(new PackageManagerSource()) },
            new UninstallRouter(new ShellProcessRunner(), new PackageManagerRemover(), new ShellUrlLauncher()),
            ResidueRoots.Real())
    {
    }

    private UninstallerModule(IReadOnlyList<IAppCatalog> catalogs, UninstallRouter router, ResidueRoots roots)
        : this(catalogs, router, roots, new ShellRecycleBin(), RealElevationClient())
    {
    }

    // Shared by the residue remover and the quarantine inspect/purge calls, so they reuse the same elevated
    // helper process instead of each launching their own.
    private static IElevationClient RealElevationClient() => new ElevationClient(
        ElevationClient.NewSessionPipeName(),
        new ElevatedHelperProcessLauncher(Path.Combine(AppContext.BaseDirectory, "PowerTools.ElevatedHelper.exe")));

    private UninstallerModule(IReadOnlyList<IAppCatalog> catalogs, UninstallRouter router, ResidueRoots roots, IRecycleBin recycleBin, IElevationClient elevation)
        : this(catalogs, router, new ResidueScanner(roots), new ResidueRemover(recycleBin, elevation, roots), recycleBin: recycleBin, elevation: elevation)
    {
    }

    public UninstallerModule(IEnumerable<IAppCatalog> catalogs, UninstallRouter router, ResidueScanner scanner, ResidueRemover remover,
        IAppIconProvider? icons = null, Func<TimeSpan, CancellationToken, Task>? delay = null, WatchOptions? watchOptions = null,
        IRecycleBin? recycleBin = null, IElevationClient? elevation = null)
    {
        _catalogs = catalogs.ToList();
        _router = router;
        _scanner = scanner;
        _remover = remover;
        _icons = icons ?? new AppIconProvider(new ShellIconExtractor());
        _delay = delay ?? Task.Delay;
        _watchOptions = watchOptions ?? WatchOptions.Default;
        _recycleBin = recycleBin ?? new ShellRecycleBin();
        _elevation = elevation ?? RealElevationClient();
    }

    public string ModuleId => "uninstaller";

    public string DisplayName => "卸载工具";

    // Nothing can fail up front: each source is read on demand and a source that cannot be read just contributes nothing.
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<InstalledApp>> LoadAppsAsync() => Task.Run<IReadOnlyList<InstalledApp>>(() =>
    {
        var apps = new List<InstalledApp>();
        foreach (var catalog in _catalogs)
        {
            try
            {
                apps.AddRange(catalog.LoadAll());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // One broken source (e.g. the package manager refusing) must not hide the other sources.
            }
        }

        return apps.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    });

    /// <summary>Uninstalls the app; for a desktop program that uninstalled cleanly, also looks for what it left behind.</summary>
    public async Task<UninstallResult> UninstallAsync(InstalledApp app, bool asAdministrator = false, CancellationToken ct = default)
    {
        // Steam cleans up after its own games and the system after Store packages; only desktop programs get a scan.
        var before = app.Source == AppSource.Win32 ? await Task.Run(() => _scanner.Capture(app), ct) : null;

        var outcome = await _router.UninstallAsync(app, asAdministrator, ct);

        var leftovers = before is not null && outcome.Status == UninstallStatus.Completed
            ? await Task.Run(() => _scanner.FindLeftovers(app, before), ct)
            : Array.Empty<ResidueItem>();

        // MSI/Squirrel-style launchers exit at once and finish in the background; Steam itself also only hands off
        // to its own window and returns immediately. Either way the caller cannot know "done" synchronously, so if
        // some catalog can tell when the program disappears from the installed list, wait for that before telling
        // the caller to refresh (and, for a desktop program, scan for leftovers once it is really gone).
        var deferred = outcome.Status == UninstallStatus.MaybeStillRunning && IsInstalled(app) is not null
            ? (Func<CancellationToken, Task<IReadOnlyList<ResidueItem>>>)(token => ScanWhenFinishedAsync(app, before, token))
            : null;
        return new UninstallResult(outcome, leftovers, deferred);
    }

    private bool? IsInstalled(InstalledApp app)
    {
        var answers = _catalogs.Select(c => c.IsInstalled(app)).Where(a => a.HasValue).ToList();
        return answers.Count == 0 ? null : answers.Any(a => a == true);
    }

    // Waits (bounded) until the program is gone from the installed list, gives the uninstaller a moment to finish its
    // last deletions, then scans (if there is a snapshot to compare against at all; Steam/Store clean up their own
    // files, so there is nothing to scan for them — the wait itself is still useful so the caller knows to refresh).
    private async Task<IReadOnlyList<ResidueItem>> ScanWhenFinishedAsync(InstalledApp app, ResidueSnapshot? before, CancellationToken ct)
    {
        var limits = _watchOptions with { MaxDuration = TimeSpan.FromMinutes(2) };
        if (!await LeftoverWatcher.WaitUntilAsync(() => IsInstalled(app) != true, limits, _delay, ct)) return Array.Empty<ResidueItem>();
        if (before is null) return Array.Empty<ResidueItem>();

        try
        {
            await _delay(TimeSpan.FromSeconds(3), ct);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<ResidueItem>();
        }

        return await Task.Run(() => _scanner.FindLeftovers(app, before), ct);
    }

    /// <summary>
    /// Keeps the list honest while the uninstaller finishes cleaning up on its own: calls <paramref name="onShrunk"/>
    /// (from a background thread) with what is still there whenever items have disappeared. Bounded in time and cost,
    /// see <see cref="LeftoverWatcher"/>.
    /// </summary>
    public Task WatchLeftoversAsync(IReadOnlyList<ResidueItem> items, Action<IReadOnlyList<ResidueItem>> onShrunk, CancellationToken ct = default)
        => Task.Run(() => LeftoverWatcher.WatchAsync(items, item => !_remover.IsGone(item), onShrunk, _watchOptions, _delay, ct), ct);

    /// <summary>
    /// Real size of the program's folder (for a Store app, plus its data), for entries that have no recorded size; null when
    /// the folder is unknown.
    /// </summary>
    public Task<long?> MeasureSizeAsync(InstalledApp app, CancellationToken ct = default)
        => Task.Run(() => AppSizer.Measure(app, ct), ct);

    /// <summary>The app's icon as PNG bytes; null when none can be found, so the caller shows a placeholder.</summary>
    public Task<byte[]?> GetIconPngAsync(InstalledApp app, CancellationToken ct = default) => _icons.GetPngAsync(app, ct);

    /// <summary>The installed program a dropped file, shortcut or folder belongs to.</summary>
    public InstalledApp? FindAppByPath(IEnumerable<InstalledApp> apps, string path)
        => ShortcutResolver.Resolve(path) is { } target ? AppMatcher.Find(apps, target) : null;

    /// <summary>Where the .reg backups of removed registry keys are written (double-click one to restore).</summary>
    public string RegistryBackupDirectory => _remover.BackupDirectory;

    public Task<IReadOnlyList<RemovalResult>> RemoveResidueAsync(IReadOnlyList<ResidueItem> items, CancellationToken ct = default)
        => _remover.RemoveAsync(items, ct);

    /// <summary>Empties the Recycle Bin (on every drive), so folders moved there by a previous removal are actually freed.</summary>
    public Task EmptyRecycleBinAsync(CancellationToken ct = default) => Task.Run(_recycleBin.Empty, ct);

    /// <summary>How much the Recycle Bin (every drive) currently holds, so the user can decide whether to empty it.</summary>
    public Task<RecycleBinUsage> GetRecycleBinUsageAsync(CancellationToken ct = default) => Task.Run(_recycleBin.GetUsage, ct);

    /// <summary>How much the quarantine folder (admin-only residue removals) is currently holding.</summary>
    public async Task<QuarantineSummary> InspectQuarantineAsync(CancellationToken ct = default)
    {
        var response = await _elevation.ExecuteAsync(new ElevationRequest("quarantine.inspect", "{}"), ct);
        if (!response.Success || response.ResultJson is null) return new QuarantineSummary(0, 0);

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<QuarantineSummary>(response.ResultJson) ?? new QuarantineSummary(0, 0);
        }
        catch (System.Text.Json.JsonException)
        {
            return new QuarantineSummary(0, 0);
        }
    }

    /// <summary>Really deletes everything in the quarantine folder. Only call this after the user has explicitly confirmed.</summary>
    public async Task<bool> PurgeQuarantineAsync(CancellationToken ct = default)
    {
        var response = await _elevation.ExecuteAsync(new ElevationRequest("quarantine.purge", "{}"), ct);
        return response.Success;
    }
}

/// <param name="TotalBytes">Total size of everything currently quarantined.</param>
/// <param name="BatchCount">Number of past cleanup operations still sitting in quarantine.</param>
public record QuarantineSummary(long TotalBytes, int BatchCount);
