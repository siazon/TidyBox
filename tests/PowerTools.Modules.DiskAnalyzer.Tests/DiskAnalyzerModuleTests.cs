using PowerTools.Core.Modules;
using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests;

public class DiskAnalyzerModuleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pt-module-" + Guid.NewGuid());
    // Any test that calls GetSteamGames()/GetWorkshopResidues() must inject this instead of the real default
    // SteamAppCache, which otherwise writes to the real %LOCALAPPDATA% on whatever machine runs the tests.
    private readonly string _cacheFile = Path.Combine(Path.GetTempPath(), "pt-module-cache-" + Guid.NewGuid() + ".json");

    public DiskAnalyzerModuleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        if (File.Exists(_cacheFile)) File.Delete(_cacheFile);
    }

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
        public int EmptyCalls;
        public void Delete(string folderPath) => Deleted.Add(folderPath);
        public void Empty() => EmptyCalls++;
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

    private static string MakeWorkshopFolder(string libraryDir, string appId)
    {
        var path = Path.Combine(libraryDir, "steamapps", "workshop", "content", appId);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "mod.bin"), new byte[10]);
        return path;
    }

    private static void WriteManifest(string libraryDir, string appId, string installDir)
    {
        const string template = """
            "AppState"
            {
                "appid"		"__APPID__"
                "name"		"__NAME__"
                "installdir"		"__NAME__"
                "SizeOnDisk"		"1"
                "LastUpdated"		"1700000000"
            }
            """;
        var content = template.Replace("__APPID__", appId).Replace("__NAME__", installDir);
        Directory.CreateDirectory(Path.Combine(libraryDir, "steamapps", "common", installDir));
        File.WriteAllText(Path.Combine(libraryDir, "steamapps", $"appmanifest_{appId}.acf"), content);
    }

    [Fact]
    public async Task GetWorkshopResidues_GameNotInstalled_IsReported()
    {
        var workshop = MakeWorkshopFolder(_dir, "1");
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, steamAppCache: new SteamAppCache(_cacheFile));
        await module.InitializeAsync();

        var residues = module.GetWorkshopResidues();

        Assert.Equal(workshop, Assert.Single(residues).FullPath);
    }

    [Fact]
    public async Task GetWorkshopResidues_GameStillInstalled_IsExcluded()
    {
        WriteManifest(_dir, "1", "GameOne");
        MakeWorkshopFolder(_dir, "1");
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, steamAppCache: new SteamAppCache(_cacheFile));
        await module.InitializeAsync();

        Assert.Empty(module.GetWorkshopResidues());
    }

    [Fact]
    public async Task RemoveWorkshopResidueAsync_PathUnderCurrentLibrary_DeletesThroughRecycleBin()
    {
        var workshop = MakeWorkshopFolder(_dir, "1");
        var recycleBin = new FakeRecycleBin();
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, recycleBin: recycleBin, steamAppCache: new SteamAppCache(_cacheFile));
        await module.InitializeAsync();
        var item = Assert.Single(module.GetWorkshopResidues());

        await module.RemoveWorkshopResidueAsync(item);

        Assert.Equal(new[] { workshop }, recycleBin.Deleted);
    }

    [Fact]
    public async Task RemoveWorkshopResidueAsync_PathNotUnderAnyKnownLibrary_IsRejectedWithoutCallingRecycleBin()
    {
        var recycleBin = new FakeRecycleBin();
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, recycleBin: recycleBin);
        await module.InitializeAsync();
        var tampered = new WorkshopResidueItem(_dir, "1", null, @"C:\Windows\System32", 0);

        await Assert.ThrowsAsync<ArgumentException>(() => module.RemoveWorkshopResidueAsync(tampered));
        Assert.Empty(recycleBin.Deleted);
    }
}
