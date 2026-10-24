using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer.Tests.Scanning;

public class DirectoryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pt-scan-" + Guid.NewGuid());
    private readonly List<string> _deniedDirs = new();

    public DirectoryScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var dir in _deniedDirs)
        {
            var info = new DirectoryInfo(dir);
            var security = info.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Deny) security.RemoveAccessRule(rule);
            }
            info.SetAccessControl(security);
        }
        Directory.Delete(_root, recursive: true);
    }

    private string WriteFile(string relativePath, int bytes)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    // Real ACL deny for the current user: listing the directory then throws UnauthorizedAccessException.
    private string DenyListing(string relativePath)
    {
        var dir = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(dir);
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        info.SetAccessControl(security);
        _deniedDirs.Add(dir);

        // Guard: the tests below are only meaningful if the deny really took effect.
        Assert.ThrowsAny<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(dir).ToList());
        return dir;
    }

    [Fact]
    public async Task ScanAsync_TwoSubfolders_ReturnsAggregatedSizePerFolder()
    {
        WriteFile(@"A\a.txt", 100);
        WriteFile(@"A\a2.txt", 200);
        WriteFile(@"B\b.txt", 50);

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        var a = snapshot.Entries.Single(e => e.Path.EndsWith(@"\A"));
        var b = snapshot.Entries.Single(e => e.Path.EndsWith(@"\B"));
        Assert.Equal(300, a.SizeBytes);
        Assert.Equal(2, a.ItemCount);
        Assert.Equal(50, b.SizeBytes);
        Assert.Equal(_root, snapshot.RootPath);
    }

    [Fact]
    public async Task ScanAsync_NestedFiles_AreIncludedInTopLevelEntry()
    {
        WriteFile(@"A\x\y\deep.txt", 70);
        WriteFile(@"A\top.txt", 30);

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(100, snapshot.Entries.Single().SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_EntriesAreOrderedBySizeDescending()
    {
        WriteFile(@"Small\s.txt", 10);
        WriteFile(@"Big\b.txt", 1000);

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(new[] { "Big", "Small" }, snapshot.Entries.Select(e => Path.GetFileName(e.Path)));
    }

    [Fact]
    public async Task ScanAsync_LastModified_IsNewestFileInSubtree()
    {
        var older = WriteFile(@"A\old.txt", 1);
        var newer = WriteFile(@"A\sub\new.txt", 1);
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.Entries.Single().LastModifiedUtc);
    }

    [Fact]
    public async Task ScanAsync_LockedDirectChild_DoesNotThrowAndOthersAreScanned()
    {
        WriteFile(@"Readable\x.txt", 10);
        DenyListing("Locked");

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(10, snapshot.Entries.Single(e => e.Path.EndsWith("Readable")).SizeBytes);
        Assert.All(snapshot.Entries.Where(e => e.Path.EndsWith("Locked")), e => Assert.Equal(0, e.SizeBytes));
    }

    [Fact]
    public async Task ScanAsync_LockedGrandchild_StillCountsSiblingFilesInTheSameTopLevelFolder()
    {
        WriteFile(@"A\ok.txt", 100);
        WriteFile(@"A\Locked\hidden.txt", 999);
        DenyListing(@"A\Locked");

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(100, snapshot.Entries.Single().SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_HiddenFiles_AreCounted()
    {
        var path = WriteFile(@"A\hidden.txt", 40);
        File.SetAttributes(path, FileAttributes.Hidden);

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(40, snapshot.Entries.Single().SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_Junction_IsNotFollowed()
    {
        WriteFile(@"Real\r.txt", 25);
        var target = Path.Combine(_root, "Real");
        Directory.CreateDirectory(Path.Combine(_root, "Holder"));
        var junction = Path.Combine(_root, "Holder", "link");
        var mklink = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{junction}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);

        var snapshot = await new DirectoryScanner().ScanAsync(_root);

        Assert.Equal(0, snapshot.Entries.Single(e => e.Path.EndsWith("Holder")).SizeBytes);
        Assert.Equal(25, snapshot.Entries.Single(e => e.Path.EndsWith("Real")).SizeBytes);
        Directory.Delete(junction); // removes only the link, not the target
    }

    [Fact]
    public async Task ScanAsync_Cancelled_ThrowsOperationCanceled()
    {
        WriteFile(@"A\a.txt", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DirectoryScanner().ScanAsync(_root, cts.Token));
    }
}
