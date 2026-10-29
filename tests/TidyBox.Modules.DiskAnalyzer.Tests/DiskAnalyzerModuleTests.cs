using TidyBox.Core.Modules;
using TidyBox.Modules.DiskAnalyzer.Steam;

namespace TidyBox.Modules.DiskAnalyzer.Tests;

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

    private sealed class FakeRecycleBin : TidyBox.Core.Recycling.IRecycleBin
    {
        public List<string> Deleted = new();
        public int EmptyCalls;
        public TidyBox.Core.Recycling.RecycleBinUsage Usage = new(0, 0);
        public void Delete(string folderPath) => Deleted.Add(folderPath);
        public void Empty() => EmptyCalls++;
        public TidyBox.Core.Recycling.RecycleBinUsage GetUsage() => Usage;
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

    private async Task<DiskAnalyzerModule> RelinkModule()
    {
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, steamAppCache: new SteamAppCache(_cacheFile));
        await module.InitializeAsync();
        return module;
    }

    [Fact]
    public async Task RelinkOrphanFolder_WritesManifest_SoTheFolderIsAnInstalledGameAndNoLongerAnOrphan()
    {
        MakeGhostFolder(_dir);
        var module = await RelinkModule();
        var folder = Assert.Single(module.GetOrphanFolders());

        module.RelinkOrphanFolder(folder, "413150");

        var game = Assert.Single(module.GetSteamGames());
        Assert.Equal("413150", game.AppId);
        Assert.Equal("GhostFolder", game.Name); // no cached name -> falls back to the folder name
        Assert.Equal(folder.FullPath, game.InstallPath);
        Assert.Empty(module.GetOrphanFolders());
    }

    [Fact]
    public async Task RelinkOrphanFolder_UsesCachedNameWhenKnown()
    {
        MakeGhostFolder(_dir);
        new SteamAppCache(_cacheFile).Upsert("413150", "Stardew Valley");
        var module = await RelinkModule();

        module.RelinkOrphanFolder(Assert.Single(module.GetOrphanFolders()), "413150");

        Assert.Equal("Stardew Valley", Assert.Single(module.GetSteamGames()).Name);
    }

    [Fact]
    public async Task RelinkOrphanFolder_AppIdAlreadyInstalledElsewhere_IsRejectedWithoutWriting()
    {
        MakeGhostFolder(_dir);
        var module = await RelinkModule();
        var folder = Assert.Single(module.GetOrphanFolders());
        File.WriteAllText(Path.Combine(_dir, "steamapps", "appmanifest_730.acf"),
            "\"AppState\"\n{\n\"appid\" \"730\"\n\"name\" \"CS\"\n\"installdir\" \"CS\"\n\"SizeOnDisk\" \"1\"\n\"LastUpdated\" \"1700000000\"\n}");

        Assert.Throws<InvalidOperationException>(() => module.RelinkOrphanFolder(folder, "730"));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "steamapps"), "appmanifest_*.acf"));
    }

    [Fact]
    public async Task RelinkOrphanFolder_InvalidAppId_Throws()
    {
        MakeGhostFolder(_dir);
        var module = await RelinkModule();
        var folder = Assert.Single(module.GetOrphanFolders());

        Assert.Throws<ArgumentException>(() => module.RelinkOrphanFolder(folder, "not-a-number"));
    }

    [Fact]
    public async Task RelinkOrphanFolder_PathNotUnderAnyKnownLibrary_IsRejectedWithoutWriting()
    {
        var module = await RelinkModule();
        Directory.CreateDirectory(Path.Combine(_dir, "steamapps"));
        var tampered = new OrphanGameFolder(_dir, "X", @"C:\Windows\System32", 0, DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => module.RelinkOrphanFolder(tampered, "1"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "steamapps"), "appmanifest_*.acf"));
    }

    [Fact]
    public async Task GuessAppId_SteamAppIdTxtInFolder_IsUsed()
    {
        var ghost = MakeGhostFolder(_dir);
        File.WriteAllText(Path.Combine(ghost, "steam_appid.txt"), "413150\r\n");
        Directory.SetLastWriteTimeUtc(ghost, DateTime.UtcNow.AddDays(-1)); // writing the file just refreshed the "recently changed" guard
        var module = await RelinkModule();

        Assert.Equal("413150", module.GuessAppId(Assert.Single(module.GetOrphanFolders())));
    }

    [Fact]
    public async Task GuessAppId_NoFile_FallsBackToCachedInstallDir()
    {
        MakeGhostFolder(_dir);
        new SteamAppCache(_cacheFile).Upsert("99", "Ghost Game", "GhostFolder");
        var module = await RelinkModule();

        Assert.Equal("99", module.GuessAppId(Assert.Single(module.GetOrphanFolders())));
    }

    [Fact]
    public async Task GuessAppId_NothingKnown_ReturnsNull()
    {
        MakeGhostFolder(_dir);
        var module = await RelinkModule();

        Assert.Null(module.GuessAppId(Assert.Single(module.GetOrphanFolders())));
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

    [Fact]
    public async Task EmptyRecycleBinAsync_CallsRecycleBinEmpty()
    {
        var recycleBin = new FakeRecycleBin();
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, recycleBin: recycleBin);

        await module.EmptyRecycleBinAsync();

        Assert.Equal(1, recycleBin.EmptyCalls);
    }

    [Fact]
    public async Task GetRecycleBinUsageAsync_ReturnsTheRecycleBinsUsage()
    {
        var recycleBin = new FakeRecycleBin { Usage = new TidyBox.Core.Recycling.RecycleBinUsage(12345, 3) };
        var module = new DiskAnalyzerModule(steamInstallPathOverride: _dir, recycleBin: recycleBin);

        var usage = await module.GetRecycleBinUsageAsync();

        Assert.Equal(12345, usage.TotalBytes);
        Assert.Equal(3, usage.ItemCount);
    }
}
