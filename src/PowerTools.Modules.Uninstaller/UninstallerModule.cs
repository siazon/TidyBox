using PowerTools.Core.Elevation;
using PowerTools.Core.Modules;
using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Icons;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Residue;
using PowerTools.Modules.Uninstaller.Routing;

namespace PowerTools.Modules.Uninstaller;

/// <param name="Outcome">How the uninstall itself went.</param>
/// <param name="Leftovers">What is still on disk (or in the registry) afterwards; empty unless a desktop program uninstalled cleanly.</param>
public record UninstallResult(UninstallOutcome Outcome, IReadOnlyList<ResidueItem> Leftovers);

public sealed class UninstallerModule : IToolModule
{
    private readonly IReadOnlyList<IAppCatalog> _catalogs;
    private readonly UninstallRouter _router;
    private readonly ResidueScanner _scanner;
    private readonly ResidueRemover _remover;
    private readonly IAppIconProvider _icons;

    public UninstallerModule()
        : this(
            new IAppCatalog[] { Win32AppCatalog.Real(), new StoreAppCatalog(new PackageManagerSource()) },
            new UninstallRouter(new ShellProcessRunner(), new PackageManagerRemover(), new ShellUrlLauncher()),
            ResidueRoots.Real())
    {
    }

    private UninstallerModule(IReadOnlyList<IAppCatalog> catalogs, UninstallRouter router, ResidueRoots roots)
        : this(
            catalogs,
            router,
            new ResidueScanner(roots),
            new ResidueRemover(
                new ShellRecycleBin(),
                new ElevationClient(
                    ElevationClient.NewSessionPipeName(),
                    new ElevatedHelperProcessLauncher(Path.Combine(AppContext.BaseDirectory, "PowerTools.ElevatedHelper.exe"))),
                roots))
    {
    }

    public UninstallerModule(IEnumerable<IAppCatalog> catalogs, UninstallRouter router, ResidueScanner scanner, ResidueRemover remover,
        IAppIconProvider? icons = null)
    {
        _catalogs = catalogs.ToList();
        _router = router;
        _scanner = scanner;
        _remover = remover;
        _icons = icons ?? new AppIconProvider(new ShellIconExtractor());
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
    public async Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct = default)
    {
        // Steam cleans up after its own games and the system after Store packages; only desktop programs get a scan.
        var before = app.Source == AppSource.Win32 ? await Task.Run(() => _scanner.Capture(app), ct) : null;

        var outcome = await _router.UninstallAsync(app, ct);

        var leftovers = before is not null && outcome.Status == UninstallStatus.Completed
            ? await Task.Run(() => _scanner.FindLeftovers(app, before), ct)
            : Array.Empty<ResidueItem>();
        return new UninstallResult(outcome, leftovers);
    }

    /// <summary>Real size of the program's folder, for entries whose registry record has no size; null when the folder is unknown.</summary>
    public Task<long?> MeasureSizeAsync(InstalledApp app, CancellationToken ct = default)
        => Task.Run(() => AppFolder.Resolve(app) is { } folder ? FolderSizer.Measure(folder, ct) : null, ct);

    /// <summary>The app's icon as PNG bytes; null when none can be found, so the caller shows a placeholder.</summary>
    public Task<byte[]?> GetIconPngAsync(InstalledApp app, CancellationToken ct = default) => _icons.GetPngAsync(app, ct);

    /// <summary>The installed program a dropped file, shortcut or folder belongs to.</summary>
    public InstalledApp? FindAppByPath(IEnumerable<InstalledApp> apps, string path)
        => ShortcutResolver.Resolve(path) is { } target ? AppMatcher.Find(apps, target) : null;

    public Task<IReadOnlyList<RemovalResult>> RemoveResidueAsync(IReadOnlyList<ResidueItem> items, CancellationToken ct = default)
        => _remover.RemoveAsync(items, ct);
}
