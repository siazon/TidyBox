using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests;

public class DiskAnalyzerModuleWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pt-flow-" + Guid.NewGuid());
    private string Snapshots => Path.Combine(_root, "snapshots");
    private string Data => Path.Combine(_root, "data");
    private string Steam => Path.Combine(_root, "steam");

    public DiskAnalyzerModuleWorkflowTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Path.Combine(Steam, "steamapps"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private class FakeLauncher : ISteamUninstallLauncher
    {
        public List<string> Launched { get; } = new();
        public void LaunchUninstall(string appId) => Launched.Add(appId);
    }

    private DiskAnalyzerModule NewModule(string? steam = null, ISteamUninstallLauncher? launcher = null)
        => new(steamInstallPathOverride: steam ?? @"C:\DoesNotExist", snapshotDirectory: Snapshots, steamUninstallLauncher: launcher);

    private void WriteData(string relativePath, int bytes)
    {
        var path = Path.Combine(Data, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private static void WriteGame(string libraryRoot, string appId, string name, long size)
    {
        var steamApps = Path.Combine(libraryRoot, "steamapps");
        Directory.CreateDirectory(steamApps);
        File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\"{appId}\"\n\t\"name\"\t\"{name}\"\n\t\"SizeOnDisk\"\t\"{size}\"\n\t\"LastUpdated\"\t\"1700000000\"\n}}");
    }

    // VDF escapes backslashes, so a path like C:\a\b must be written as C:\\a\\b.
    private static string Vdf(string path) => path.Replace("\\", "\\\\");

    private void WriteLibraryFolders(params string[] libraryPaths)
    {
        var entries = string.Join("\n", libraryPaths.Select((p, i) => $"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\"{Vdf(p)}\"\n\t}}"));
        File.WriteAllText(Path.Combine(Steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n" + entries + "\n}");
    }

    [Fact]
    public async Task ScanAsync_FirstScan_HasNoPreviousAndNoChanges()
    {
        WriteData(@"A\a.txt", 100);
        var module = NewModule();
        await module.InitializeAsync();

        var result = await module.ScanAsync(Data);

        Assert.Equal(100, result.Current.Entries.Single().SizeBytes);
        Assert.Null(result.Previous);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task ScanAsync_SecondScan_ReportsGrowthAgainstThePreviousScan()
    {
        WriteData(@"A\a.txt", 100);
        WriteData(@"B\b.txt", 10);
        var module = NewModule();
        await module.InitializeAsync();
        await module.ScanAsync(Data);

        WriteData(@"A\more.txt", 400);
        var second = await module.ScanAsync(Data);

        Assert.NotNull(second.Previous);
        var top = second.Changes.First();
        Assert.EndsWith(@"\A", top.Path);
        Assert.Equal(400, top.SizeDeltaBytes);
    }

    [Fact]
    public async Task ScanAsync_HistorySurvivesANewModuleInstance()
    {
        WriteData(@"A\a.txt", 100);
        var first = NewModule();
        await first.InitializeAsync();
        await first.ScanAsync(Data);

        var second = NewModule();
        await second.InitializeAsync();
        var result = await second.ScanAsync(Data);

        Assert.NotNull(result.Previous);
    }

    [Fact]
    public async Task InitializeAsync_SnapshotFolderCannotBeCreated_ThrowsClearException()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");
        var module = new DiskAnalyzerModule(steamInstallPathOverride: @"C:\DoesNotExist", snapshotDirectory: Path.Combine(blocker, "snapshots"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => module.InitializeAsync());

        Assert.Contains("snapshots", ex.Message);
    }

    [Fact]
    public async Task GetSteamGames_SteamNotAvailable_ReturnsEmpty()
    {
        var module = NewModule(steam: @"C:\DoesNotExist");
        await module.InitializeAsync();

        Assert.Empty(module.GetSteamGames());
    }

    [Fact]
    public async Task GetSteamGames_GamesFromAllLibraries_AreListedLargestFirst()
    {
        var otherLibrary = Path.Combine(_root, "OtherLibrary");
        WriteLibraryFolders(Steam, otherLibrary);
        WriteGame(Steam, "1", "Small", 10);
        WriteGame(otherLibrary, "2", "Big", 1000);
        var module = NewModule(steam: Steam);
        await module.InitializeAsync();

        var games = module.GetSteamGames();

        Assert.Equal(new[] { "Big", "Small" }, games.Select(g => g.Name));
    }

    [Fact]
    public async Task GetSteamGames_NoLibraryFoldersFile_FallsBackToTheInstallFolder()
    {
        WriteGame(Steam, "1", "Only", 10);
        var module = NewModule(steam: Steam);
        await module.InitializeAsync();

        Assert.Equal("Only", module.GetSteamGames().Single().Name);
    }

    [Fact]
    public async Task GetSteamGames_SameLibraryListedTwice_ReturnsEachGameOnce()
    {
        WriteLibraryFolders(Steam, Steam.ToUpperInvariant());
        WriteGame(Steam, "1", "Once", 10);
        var module = NewModule(steam: Steam);
        await module.InitializeAsync();

        Assert.Single(module.GetSteamGames());
    }

    [Fact]
    public void UninstallSteamGame_DelegatesToTheLauncher()
    {
        var launcher = new FakeLauncher();
        var module = NewModule(launcher: launcher);

        module.UninstallSteamGame("730");

        Assert.Equal(new[] { "730" }, launcher.Launched);
    }
}
