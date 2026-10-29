using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tests.Scanning;

public class DownloadScannerTests : IDisposable
{
    private static readonly TimeSpan Window = TidyOptions.Default.RecentWindow;

    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Folders = { "文档", "安装包" };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public DownloadScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[10]);
        File.SetLastWriteTimeUtc(path, Now - TimeSpan.FromDays(1));
        return path;
    }

    private IReadOnlyList<DownloadFile> Scan() => new DownloadScanner().Scan(_root, Folders, Now, Window);

    [Fact]
    public void ReturnsTopLevelFiles_WithNullCategoryFolder()
    {
        var path = Make("a.pdf");

        var file = Assert.Single(Scan());

        Assert.Equal((path, 10L, null), (file.FullPath, file.SizeBytes, file.CategoryFolder));
    }

    [Fact]
    public void ReturnsFilesOneLevelInsideCategoryFolders_WithThatFolderName()
    {
        Make(Path.Combine("文档", "a.pdf"));

        var file = Assert.Single(Scan());

        Assert.Equal("文档", file.CategoryFolder);
    }

    [Fact]
    public void DoesNotDescendTwoLevels_NorIntoOtherFolders()
    {
        Make(Path.Combine("我的项目", "x.pdf"));
        Make(Path.Combine("文档", "sub", "y.pdf"));

        Assert.Empty(Scan());
    }

    [Fact]
    public void SkipsCategoryFolderThatIsAReparsePoint()
    {
        var target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "z.pdf"), new byte[10]);
        File.SetLastWriteTimeUtc(Path.Combine(target, "z.pdf"), Now - TimeSpan.FromDays(1));

        if (!TestLinks.TryCreateDirectoryLink(Path.Combine(_root, "文档"), target)) return;

        Assert.Empty(Scan());
    }

    [Fact]
    public void MissingRoot_ReturnsEmpty()
    {
        Assert.Empty(new DownloadScanner().Scan(Path.Combine(_root, "nope"), Folders, Now, Window));
    }

    [Fact]
    public void IneligibleFiles_AreLeftOut()
    {
        Make("movie.crdownload");
        Make(Path.Combine("文档", "Thumbs.db"));

        Assert.Empty(Scan());
    }
}
