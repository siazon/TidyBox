using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests.Steam;

public class WorkshopResidueScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pt-workshop-" + Guid.NewGuid());

    public WorkshopResidueScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Library(string name = "Lib")
    {
        var lib = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "workshop", "content"));
        return lib;
    }

    private static string WorkshopFolder(string library, string appId)
    {
        var path = Path.Combine(library, "steamapps", "workshop", "content", appId);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "mod.bin"), new byte[100]);
        return path;
    }

    [Fact]
    public void Scan_AppIdNotInstalled_IsReportedAsResidue()
    {
        var lib = Library();
        WorkshopFolder(lib, "413150");

        var residues = new WorkshopResidueScanner().Scan(new[] { lib }, installedAppIds: Array.Empty<string>());

        Assert.Equal("413150", Assert.Single(residues).AppId);
    }

    [Fact]
    public void Scan_AppIdStillInstalled_IsNotReported()
    {
        var lib = Library();
        WorkshopFolder(lib, "413150");

        var residues = new WorkshopResidueScanner().Scan(new[] { lib }, installedAppIds: new[] { "413150" });

        Assert.Empty(residues);
    }

    [Fact]
    public void Scan_NoWorkshopContentFolder_ReturnsEmpty()
    {
        var lib = Path.Combine(_root, "EmptyLib");
        Directory.CreateDirectory(lib);

        var residues = new WorkshopResidueScanner().Scan(new[] { lib }, installedAppIds: Array.Empty<string>());

        Assert.Empty(residues);
    }

    [Fact]
    public void Scan_NameInCache_IsIncludedInResult()
    {
        var lib = Library();
        WorkshopFolder(lib, "413150");
        var cache = new FakeNameLookup(new() { ["413150"] = "Stardew Valley" });

        var residues = new WorkshopResidueScanner(nameLookup: cache).Scan(new[] { lib }, installedAppIds: Array.Empty<string>());

        Assert.Equal("Stardew Valley", Assert.Single(residues).GameName);
    }

    [Fact]
    public void Scan_NameNotInCache_LeavesGameNameNull()
    {
        var lib = Library();
        WorkshopFolder(lib, "999");

        var residues = new WorkshopResidueScanner().Scan(new[] { lib }, installedAppIds: Array.Empty<string>());

        Assert.Null(Assert.Single(residues).GameName);
    }

    [Fact]
    public void Scan_MeasuresSize()
    {
        var lib = Library();
        WorkshopFolder(lib, "413150");

        var residues = new WorkshopResidueScanner().Scan(new[] { lib }, installedAppIds: Array.Empty<string>());

        Assert.Equal(100, Assert.Single(residues).SizeBytes);
    }

    private sealed class FakeNameLookup : ISteamAppNameLookup
    {
        private readonly Dictionary<string, string> _names;
        public FakeNameLookup(Dictionary<string, string> names) => _names = names;
        public string? TryGetName(string appId) => _names.TryGetValue(appId, out var n) ? n : null;
    }
}
