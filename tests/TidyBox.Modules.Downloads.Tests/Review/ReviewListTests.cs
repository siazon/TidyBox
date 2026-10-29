using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tests.Review;

public class ReviewListTests : IDisposable
{
    private static readonly TimeSpan Stale = TidyOptions.Default.StaleAfter;

    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public ReviewListTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private KeptStore NewStore() => new(Path.Combine(_dir, "kept.json"));

    private static DownloadFile File(string name, long size, int daysOld, string? folder = null)
    {
        var when = Now - TimeSpan.FromDays(daysOld);
        return new DownloadFile(@"C:\d\" + name, size, when, when, folder);
    }

    [Fact]
    public void Build_ExcludesKeptFromStale_AndListsThemInKept()
    {
        var store = NewStore();
        var kept = File("keep.exe", 50, 200);
        store.Keep(kept);

        var result = ReviewList.Build(new[] { kept, File("old.exe", 40, 200) }, store, Now, Stale);

        Assert.Equal("old.exe", Assert.Single(result.Stale).Name);
        Assert.Equal("keep.exe", Assert.Single(result.Kept).Name);
    }

    [Fact]
    public void Build_ExcludesFilesYoungerThanNinetyDays()
    {
        var result = ReviewList.Build(new[] { File("new.zip", 10, 10), File("edge.zip", 10, 90) }, NewStore(), Now, Stale);

        Assert.Empty(result.Stale);
    }

    [Fact]
    public void Build_SortsStaleBySizeDescending()
    {
        var result = ReviewList.Build(new[] { File("s.zip", 1, 200), File("l.zip", 3, 200), File("m.zip", 2, 200) }, NewStore(), Now, Stale);

        Assert.Equal(new[] { "l.zip", "m.zip", "s.zip" }, result.Stale.Select(f => f.Name));
    }

    [Fact]
    public void Build_IncludesStaleFilesInsideCategoryFolders()
    {
        var result = ReviewList.Build(new[] { File("old.exe", 5, 200, folder: "安装包") }, NewStore(), Now, Stale);

        Assert.Equal("安装包", Assert.Single(result.Stale).CategoryFolder);
    }

    [Fact]
    public void Build_KeptFileThatWasModified_ReappearsInStale()
    {
        var store = NewStore();
        store.Keep(File("a.exe", 5, 200));
        var modifiedSinceKeep = File("a.exe", 5, 200) with { ModifiedUtc = Now - TimeSpan.FromDays(150) };

        var result = ReviewList.Build(new[] { modifiedSinceKeep }, store, Now, Stale);

        Assert.Single(result.Stale);
        Assert.Empty(result.Kept);
    }
}
