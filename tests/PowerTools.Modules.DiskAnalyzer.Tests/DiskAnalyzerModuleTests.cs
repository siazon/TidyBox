using PowerTools.Core.Modules;
using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests;

public class DiskAnalyzerModuleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pt-module-" + Guid.NewGuid());

    public DiskAnalyzerModuleTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ModuleId_And_DisplayName_MatchConvention()
    {
        IToolModule module = new DiskAnalyzerModule(steamInstallPathOverride: null);

        Assert.Equal("disk-analyzer", module.ModuleId);
        Assert.Equal("磁盘空间分析", module.DisplayName);
    }

    [Fact]
    public void IsSteamAvailable_BeforeInitialize_IsFalse()
    {
        Assert.False(new DiskAnalyzerModule(steamInstallPathOverride: _dir).IsSteamAvailable);
    }

    [Fact]
    public async Task InitializeAsync_NoSteamInstalled_DoesNotThrowAndReportsNoSteam()
    {
        var module = new DiskAnalyzerModule(steamInstallPathOverride: @"C:\DoesNotExist");

        await module.InitializeAsync();

        Assert.False(module.IsSteamAvailable);
        Assert.Null(module.SteamInstallPath);
    }

    [Fact]
    public async Task InitializeAsync_SteamFolderWithoutSteamApps_ReportsNoSteam()
    {
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir);

        await module.InitializeAsync();

        Assert.False(module.IsSteamAvailable);
    }

    [Fact]
    public async Task InitializeAsync_SteamFolderWithSteamApps_ReportsSteamAndItsPath()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "steamapps"));
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir);

        await module.InitializeAsync();

        Assert.True(module.IsSteamAvailable);
        Assert.Equal(_dir, module.SteamInstallPath);
    }

    [Fact]
    public void SteamUninstallLauncher_BuildUri_UsesSteamUninstallProtocol()
    {
        Assert.Equal("steam://uninstall/730", SteamUninstallLauncher.BuildUri("730").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("730 & calc")]
    [InlineData("730/../x")]
    [InlineData("-1")]
    public void SteamUninstallLauncher_NonNumericAppId_IsRejectedBeforeAnythingIsLaunched(string appId)
    {
        Assert.Throws<ArgumentException>(() => SteamUninstallLauncher.BuildUri(appId));
        Assert.Throws<ArgumentException>(() => new SteamUninstallLauncher().LaunchUninstall(appId));
    }

    private sealed class FakeRecycleBin : PowerTools.Core.Recycling.IRecycleBin
    {
        public List<string> Deleted = new();
        public void Delete(string folderPath) => Deleted.Add(folderPath);
    }

    private static string MakeGhostFolder(string libraryDir)
    {
        var common = Path.Combine(libraryDir, "steamapps", "common");
        Directory.CreateDirectory(common);
        var ghost = Path.Combine(common, "GhostFolder");
        Directory.CreateDirectory(ghost);
        Directory.SetLastWriteTimeUtc(ghost, DateTime.UtcNow.AddDays(-1));
        return ghost;
    }

    [Fact]
    public async Task GetOrphanFolders_SteamAvailable_ReturnsScannerResults()
    {
        var ghost = MakeGhostFolder(_dir);
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir);
        await module.InitializeAsync();

        var orphans = module.GetOrphanFolders();

        Assert.Equal(ghost, Assert.Single(orphans).FullPath);
    }

    [Fact]
    public async Task GetOrphanFolders_SteamNotAvailable_ReturnsEmpty()
    {
        var module = new DiskAnalyzerModule(steamInstallPathOverride: @"C:\DoesNotExist");
        await module.InitializeAsync();

        Assert.Empty(module.GetOrphanFolders());
    }

    [Fact]
    public async Task RemoveOrphanFolderAsync_PathUnderCurrentLibrary_DeletesThroughRecycleBin()
    {
        var ghost = MakeGhostFolder(_dir);
        var recycleBin = new FakeRecycleBin();
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, recycleBin: recycleBin);
        await module.InitializeAsync();
        var folder = Assert.Single(module.GetOrphanFolders());

        await module.RemoveOrphanFolderAsync(folder);

        Assert.Equal(new[] { ghost }, recycleBin.Deleted);
    }

    [Fact]
    public async Task RemoveOrphanFolderAsync_PathNotUnderAnyKnownLibrary_IsRejectedWithoutCallingRecycleBin()
    {
        var recycleBin = new FakeRecycleBin();
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, recycleBin: recycleBin);
        await module.InitializeAsync();
        var tampered = new OrphanGameFolder(_dir, "X", @"C:\Windows\System32", 0, DateTime.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() => module.RemoveOrphanFolderAsync(tampered));
        Assert.Empty(recycleBin.Deleted);
    }
}
