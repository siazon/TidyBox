using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests.Steam;

public class OrphanFolderScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pt-orphan-" + Guid.NewGuid());

    public OrphanFolderScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Library(string name = "Lib")
    {
        var lib = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common"));
        return lib;
    }

    private static string CommonFolder(string library, string name)
    {
        var path = Path.Combine(library, "steamapps", "common", name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "dummy.txt"), "x");
        return path;
    }

    private static void MakeOld(string folderPath)
        => Directory.SetLastWriteTimeUtc(folderPath, DateTime.UtcNow.AddDays(-1));

    private static void WriteManifest(string library, string appId, string installDir)
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
        File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf"), content);
    }

    [Fact]
    public void Scan_FolderWithoutManifest_IsReportedAsOrphan()
    {
        var lib = Library();
        WriteManifest(lib, "1", "GameA");
        CommonFolder(lib, "GameA");
        var ghost = CommonFolder(lib, "GhostFolder");
        MakeOld(ghost);

        var orphans = new OrphanFolderScanner().Scan(new[] { lib });

        Assert.Equal("GhostFolder", Assert.Single(orphans).FolderName);
    }

    [Fact]
    public void Scan_FolderNameOnKnownSharedWhitelist_IsNotReported()
    {
        var lib = Library();
        var shared = CommonFolder(lib, "SteamVR");
        MakeOld(shared);

        var orphans = new OrphanFolderScanner().Scan(new[] { lib });

        Assert.Empty(orphans);
    }

    [Fact]
    public void Scan_RecentlyModifiedFolder_IsNotReported()
    {
        var lib = Library();
        CommonFolder(lib, "StillDownloading"); // fresh mtime, not aged

        var orphans = new OrphanFolderScanner().Scan(new[] { lib });

        Assert.Empty(orphans);
    }

    [Fact]
    public void Scan_OldEnoughFolder_IsReported()
    {
        var lib = Library();
        var ghost = CommonFolder(lib, "OldGhost");
        MakeOld(ghost);

        var orphans = new OrphanFolderScanner().Scan(new[] { lib });

        Assert.Equal("OldGhost", Assert.Single(orphans).FolderName);
    }

    [Fact]
    public void Scan_DuplicateLibraryPaths_ScansOnce()
    {
        var lib = Library();
        var ghost = CommonFolder(lib, "GhostFolder");
        MakeOld(ghost);

        var orphans = new OrphanFolderScanner().Scan(new[] { lib, lib + Path.DirectorySeparatorChar });

        Assert.Single(orphans);
    }

    [Fact]
    public void Scan_NoCommonFolder_ReturnsEmpty()
    {
        var lib = Path.Combine(_root, "EmptyLib");
        Directory.CreateDirectory(lib); // no steamapps/common at all

        var orphans = new OrphanFolderScanner().Scan(new[] { lib });

        Assert.Empty(orphans);
    }

    [Fact]
    public void Scan_ReportsSizeAndSortsLargestFirst()
    {
        var lib = Library();
        var small = CommonFolder(lib, "Small");
        File.WriteAllBytes(Path.Combine(small, "pad.bin"), new byte[10]);
        MakeOld(small);
        var big = CommonFolder(lib, "Big");
        File.WriteAllBytes(Path.Combine(big, "pad.bin"), new byte[1000]);
        MakeOld(big);

        var orphans = new OrphanFolderScanner().Scan(new[] { lib });

        Assert.Equal(new[] { "Big", "Small" }, orphans.Select(o => o.FolderName));
        Assert.True(orphans[0].SizeBytes > orphans[1].SizeBytes);
    }
}
