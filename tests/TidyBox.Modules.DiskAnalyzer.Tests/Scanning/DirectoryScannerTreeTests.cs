using System.Security.AccessControl;
using System.Security.Principal;
using TidyBox.Modules.DiskAnalyzer.Scanning;

namespace TidyBox.Modules.DiskAnalyzer.Tests.Scanning;

public class DirectoryScannerTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pt-tree-" + Guid.NewGuid());
    private readonly List<string> _denied = new();

    public DirectoryScannerTreeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var dir in _denied)
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

    private static DiskNode Child(DiskNode parent, string name) => parent.Children.Single(c => c.Name == name);

    [Fact]
    public async Task ScanTreeAsync_SizesAndCountsRollUpTheWholeSubtree()
    {
        WriteFile(@"A\a.txt", 100);
        WriteFile(@"A\sub\b.txt", 50);
        WriteFile(@"A\sub\deep\c.txt", 25);
        WriteFile(@"B\x.txt", 10);

        var root = await new DirectoryScanner().ScanTreeAsync(_root);

        var a = Child(root, "A");
        Assert.Equal(185, root.SizeBytes);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(4, root.FolderCount); // A, sub, deep, B
        Assert.Equal(175, a.SizeBytes);
        Assert.Equal(3, a.FileCount);
        Assert.Equal(2, a.FolderCount); // sub, deep
        Assert.Equal(75, Child(a, "sub").SizeBytes);
    }

    [Fact]
    public async Task ScanTreeAsync_NamesAndPaths()
    {
        WriteFile(@"A\a.txt", 1);

        var root = await new DirectoryScanner().ScanTreeAsync(_root);

        Assert.Equal(_root, root.Name);
        Assert.Equal(_root, root.FullPath);
        Assert.Equal(DiskNodeKind.Folder, root.Kind);
        Assert.Equal("A", Child(root, "A").Name);
        Assert.Equal(Path.Combine(_root, "A"), Child(root, "A").FullPath);
    }

    [Fact]
    public async Task ScanTreeAsync_DirectFiles_AppearAsAFilesGroupNode()
    {
        WriteFile(@"A\a1.txt", 100);
        WriteFile(@"A\a2.txt", 200);
        WriteFile(@"A\S\s.txt", 50);

        var a = Child(await new DirectoryScanner().ScanTreeAsync(_root), "A");

        var group = a.Children.Single(c => c.Kind == DiskNodeKind.FilesGroup);
        Assert.Equal("[2 个文件]", group.Name);
        Assert.Equal(300, group.SizeBytes);
        Assert.Equal(2, group.FileCount);
        Assert.Equal(0, group.FolderCount);
        Assert.Empty(group.Children);
        Assert.Equal(a.FullPath, group.FullPath);
        Assert.Equal(350, a.SizeBytes); // the group is part of the folder's size, not extra
    }

    [Fact]
    public async Task ScanTreeAsync_FolderWithoutDirectFiles_HasNoFilesGroup()
    {
        WriteFile(@"A\S\s.txt", 50);

        var a = Child(await new DirectoryScanner().ScanTreeAsync(_root), "A");

        Assert.DoesNotContain(a.Children, c => c.Kind == DiskNodeKind.FilesGroup);
    }

    [Fact]
    public async Task ScanTreeAsync_FilesDirectlyInTheRoot_AreCountedAndShownAsAFilesGroup()
    {
        WriteFile("loose.txt", 70);
        WriteFile(@"A\a.txt", 30);

        var root = await new DirectoryScanner().ScanTreeAsync(_root);

        Assert.Equal(100, root.SizeBytes);
        Assert.Equal(70, root.Children.Single(c => c.Kind == DiskNodeKind.FilesGroup).SizeBytes);
    }

    [Fact]
    public async Task ScanTreeAsync_ChildrenAreOrderedBySizeDescendingIncludingTheFilesGroup()
    {
        WriteFile(@"A\small\s.txt", 10);
        WriteFile(@"A\big\b.txt", 1000);
        WriteFile(@"A\direct.txt", 500);

        var a = Child(await new DirectoryScanner().ScanTreeAsync(_root), "A");

        Assert.Equal(new[] { "big", "[1 个文件]", "small" }, a.Children.Select(c => c.Name));
    }

    [Fact]
    public async Task ScanTreeAsync_EmptyFolder_IsAZeroSizedLeaf()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));

        var empty = Child(await new DirectoryScanner().ScanTreeAsync(_root), "Empty");

        Assert.Equal(0, empty.SizeBytes);
        Assert.Equal(0, empty.FileCount);
        Assert.Empty(empty.Children);
    }

    [Fact]
    public async Task ScanTreeAsync_LastModified_IsNewestFileInSubtree_AndFallsBackToTheFolderWhenEmpty()
    {
        var old = WriteFile(@"A\old.txt", 1);
        var recent = WriteFile(@"A\sub\recent.txt", 1);
        File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(recent, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var emptyDir = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(emptyDir);
        Directory.SetLastWriteTimeUtc(emptyDir, new DateTime(2019, 3, 3, 0, 0, 0, DateTimeKind.Utc));

        var root = await new DirectoryScanner().ScanTreeAsync(_root);

        Assert.Equal(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), Child(root, "A").LastModifiedUtc);
        Assert.Equal(new DateTime(2019, 3, 3, 0, 0, 0, DateTimeKind.Utc), Child(root, "Empty").LastModifiedUtc);
        // an empty sibling must not drag the parent's "newest file" time around
        Assert.Equal(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), root.LastModifiedUtc);
    }

    [Fact]
    public async Task ScanTreeAsync_LockedGrandchild_IsSkippedButSiblingsAreCounted()
    {
        WriteFile(@"A\ok.txt", 100);
        WriteFile(@"A\Locked\hidden.txt", 999);
        var locked = Path.Combine(_root, "A", "Locked");
        var info = new DirectoryInfo(locked);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        info.SetAccessControl(security);
        _denied.Add(locked);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(locked).ToList()); // guard

        var a = Child(await new DirectoryScanner().ScanTreeAsync(_root), "A");

        Assert.Equal(100, a.SizeBytes);
        Assert.Equal(0, Child(a, "Locked").SizeBytes);
    }

    [Fact]
    public async Task ScanTreeAsync_Cancelled_ThrowsOperationCanceled()
    {
        WriteFile(@"A\a.txt", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DirectoryScanner().ScanTreeAsync(_root, cts.Token));
    }
}
