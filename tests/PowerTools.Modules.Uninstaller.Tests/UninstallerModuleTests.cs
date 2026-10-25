using PowerTools.Core.Elevation;
using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Residue;
using PowerTools.Modules.Uninstaller.Routing;
using PowerTools.Modules.Uninstaller.Tests.Support;

namespace PowerTools.Modules.Uninstaller.Tests;

public sealed class UninstallerModuleTests : IDisposable
{
    private sealed class FakeCatalog(params InstalledApp[] apps) : IAppCatalog
    {
        public Func<bool?> Installed = () => null;

        public IReadOnlyList<InstalledApp> LoadAll() => apps;

        public bool? IsInstalled(InstalledApp app) => Installed();
    }

    private sealed class BrokenCatalog : IAppCatalog
    {
        public IReadOnlyList<InstalledApp> LoadAll() => throw new InvalidOperationException("boom");
    }

    private sealed class NoRecycleBin : IRecycleBin
    {
        public List<string> Deleted = new();

        public void Delete(string folderPath) => Deleted.Add(folderPath);
    }

    private sealed class NoElevation : IElevationClient
    {
        public Task<ElevationResponse> ExecuteAsync(ElevationRequest request, CancellationToken ct = default)
            => Task.FromResult(new ElevationResponse(false, "not expected", null));
    }

    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly string _appData;
    private readonly FakeRunner _runner = new();
    private readonly FakeUrls _urls = new();
    private readonly FakeStore _store = new();
    private readonly NoRecycleBin _bin = new();
    private Action? _onDelay;

    public UninstallerModuleTests() => _appData = Directory.CreateDirectory(Path.Combine(_base, "AppData")).FullName;

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private UninstallerModule Module(params IAppCatalog[] catalogs)
    {
        var roots = new ResidueRoots(new[] { _appData }, Array.Empty<string>(), Array.Empty<RegistryRoot>());
        return new UninstallerModule(
            catalogs,
            new UninstallRouter(_runner, _store, _urls),
            new ResidueScanner(roots),
            new ResidueRemover(_bin, new NoElevation(), roots),
            delay: (_, ct) => { ct.ThrowIfCancellationRequested(); _onDelay?.Invoke(); return Task.CompletedTask; });
    }

    private static InstalledApp App(string name, AppSource source = AppSource.Win32, string? uninstall = null, string? installLocation = null,
        string? steamId = null, string? package = null)
        => new(name, name, null, null, null, null, source, installLocation, uninstall, steamId, package);

    [Fact]
    public void Identity_is_stable()
    {
        var module = Module();

        Assert.Equal("uninstaller", module.ModuleId);
        Assert.Equal("卸载工具", module.DisplayName);
    }

    [Fact]
    public async Task LoadApps_merges_all_catalogs_sorted_by_name_and_survives_a_broken_one()
    {
        var module = Module(new FakeCatalog(App("Zeta"), App("alpha")), new BrokenCatalog(), new FakeCatalog(App("Mid", AppSource.Store)));

        var apps = await module.LoadAppsAsync();

        Assert.Equal(new[] { "alpha", "Mid", "Zeta" }, apps.Select(a => a.DisplayName));
    }

    [Fact]
    public async Task Win32_uninstall_that_completes_returns_leftovers_found_after_it()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;
        var app = App("Whatever", uninstall: "\"C:\\a\\unins.exe\"", installLocation: install);

        var result = await Module().UninstallAsync(app);

        Assert.Equal(UninstallStatus.Completed, result.Outcome.Status);
        Assert.Equal(install, Assert.Single(result.Leftovers).Path);
    }

    [Fact]
    public async Task Leftovers_that_the_uninstaller_removed_are_not_reported()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;
        _runner.Result = () =>
        {
            Directory.Delete(install, recursive: true);
            return 0;
        };
        var app = App("Whatever", uninstall: "\"C:\\a\\unins.exe\"", installLocation: install);

        var result = await Module().UninstallAsync(app);

        Assert.Equal(UninstallStatus.Completed, result.Outcome.Status);
        Assert.Empty(result.Leftovers);
    }

    [Fact]
    public async Task Failed_uninstall_returns_no_leftovers()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;
        _runner.Result = () => 1;
        var app = App("Whatever", uninstall: "\"C:\\a\\unins.exe\"", installLocation: install);

        var result = await Module().UninstallAsync(app);

        Assert.Equal(UninstallStatus.Failed, result.Outcome.Status);
        Assert.Empty(result.Leftovers);
    }

    [Fact]
    public async Task Steam_and_Store_uninstalls_do_not_scan_for_leftovers()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;

        var steam = await Module().UninstallAsync(App("Game", AppSource.Steam, installLocation: install, steamId: "220"));
        var store = await Module().UninstallAsync(App("Pkg", AppSource.Store, installLocation: install, package: "Pkg_1.0_x64__abc"));

        Assert.Empty(steam.Leftovers);
        Assert.Empty(store.Leftovers);
        Assert.Equal(UninstallStatus.Completed, steam.Outcome.Status);
        Assert.Equal(UninstallStatus.Completed, store.Outcome.Status);
    }

    [Fact]
    public async Task A_launcher_that_hands_over_to_a_child_keeps_its_status_and_reports_no_leftovers()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;
        var app = App("Whatever", uninstall: "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}", installLocation: install);

        var result = await Module().UninstallAsync(app);

        Assert.Equal(UninstallStatus.MaybeStillRunning, result.Outcome.Status);
        Assert.Empty(result.Leftovers);
    }

    private sealed class FakeIcons : PowerTools.Modules.Uninstaller.Icons.IAppIconProvider
    {
        public Task<byte[]?> GetPngAsync(InstalledApp app, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(app.DisplayName == "HasIcon" ? new byte[] { 7 } : null);
    }

    [Fact]
    public async Task GetIconPng_asks_the_icon_provider()
    {
        var roots = new ResidueRoots(new[] { _appData }, Array.Empty<string>(), Array.Empty<RegistryRoot>());
        var module = new UninstallerModule(Array.Empty<IAppCatalog>(), new UninstallRouter(_runner, _store, _urls),
            new ResidueScanner(roots), new ResidueRemover(_bin, new NoElevation(), roots), new FakeIcons());

        Assert.Equal(new byte[] { 7 }, await module.GetIconPngAsync(App("HasIcon")));
        Assert.Null(await module.GetIconPngAsync(App("Other")));
    }

    [Fact]
    public async Task MeasureSize_sums_the_install_folder_and_is_null_without_one()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Sized")).FullName;
        File.WriteAllBytes(Path.Combine(install, "a.bin"), new byte[1234]);

        Assert.Equal(1234, await Module().MeasureSizeAsync(App("Sized", installLocation: install)));
        Assert.Null(await Module().MeasureSizeAsync(App("NoFolder")));
    }

    [Fact]
    public void FindAppByPath_follows_shortcuts_to_the_program_they_start()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Dropped")).FullName;
        var exe = Path.Combine(install, "dropped.exe");
        File.WriteAllText(exe, "");
        var link = Path.Combine(_appData, "Dropped.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = exe;
        shortcut.Save();
        var apps = new[] { App("Dropped", installLocation: install), App("Other") };

        Assert.Equal("Dropped", Module().FindAppByPath(apps, link)?.DisplayName);
        Assert.Equal("Dropped", Module().FindAppByPath(apps, exe)?.DisplayName);
        Assert.Null(Module().FindAppByPath(apps, Path.Combine(_appData, "nothing.exe")));
    }

    [Fact]
    public async Task A_launcher_that_hands_over_gets_a_deferred_scan_that_waits_for_the_program_to_disappear_from_the_list()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;
        var checks = 0;
        var catalog = new FakeCatalog { Installed = () => ++checks < 4 };
        var app = App("Whatever", uninstall: "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}", installLocation: install);

        var result = await Module(catalog).UninstallAsync(app);
        Assert.Equal(UninstallStatus.MaybeStillRunning, result.Outcome.Status);
        Assert.Empty(result.Leftovers);
        Assert.NotNull(result.DeferredScan);

        var later = await result.DeferredScan!(CancellationToken.None);

        Assert.Equal(install, Assert.Single(later).Path);
        Assert.Equal(4, checks); // one probe to see whether the program can be followed at all, then the checks while waiting
    }

    [Fact]
    public async Task The_deferred_scan_gives_up_with_nothing_when_the_program_never_disappears()
    {
        var install = Directory.CreateDirectory(Path.Combine(_appData, "Odd Tool")).FullName;
        var catalog = new FakeCatalog { Installed = () => true };
        var app = App("Whatever", uninstall: "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}", installLocation: install);

        var result = await Module(catalog).UninstallAsync(app);

        Assert.Empty(await result.DeferredScan!(CancellationToken.None));
    }

    [Fact]
    public async Task No_deferred_scan_when_the_uninstall_finished_failed_or_cannot_be_followed()
    {
        var app = App("Whatever", uninstall: "\"C:\\a\\unins.exe\"");
        Assert.Null((await Module().UninstallAsync(app)).DeferredScan);

        _runner.Result = () => 1;
        Assert.Null((await Module().UninstallAsync(app)).DeferredScan);

        // No catalog can say whether the program is still installed, so there is nothing to wait for.
        var msi = App("Whatever", uninstall: "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}");
        _runner.Result = () => 0;
        Assert.Null((await Module(new FakeCatalog()).UninstallAsync(msi)).DeferredScan);
    }

    [Fact]
    public async Task WatchLeftovers_reports_items_that_disappear_while_watching()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_appData, "Fading")).FullName;
        var item = new ResidueItem(ResidueKind.Folder, folder, ResidueConfidence.Medium, "x", false, true);
        var reported = new List<int>();
        _onDelay = () => { if (Directory.Exists(folder)) Directory.Delete(folder); };

        await Module().WatchLeftoversAsync(new[] { item }, remaining => reported.Add(remaining.Count), CancellationToken.None);

        Assert.Equal(new[] { 0 }, reported);
    }

    [Fact]
    public async Task RemoveResidue_goes_through_the_remover()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_appData, "Left")).FullName;
        var item = new ResidueItem(ResidueKind.Folder, folder, ResidueConfidence.High, "x", false, true);

        var results = await Module().RemoveResidueAsync(new[] { item });

        Assert.True(Assert.Single(results).Success);
        Assert.Equal(new[] { folder }, _bin.Deleted);
    }
}
