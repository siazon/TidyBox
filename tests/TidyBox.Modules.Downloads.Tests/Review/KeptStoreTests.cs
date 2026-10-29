using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tests.Review;

public class KeptStoreTests : IDisposable
{
    private static readonly DateTime Modified = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());
    private string StorePath => Path.Combine(_dir, "kept.json");

    public KeptStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DownloadFile File(string path = @"C:\d\setup.exe", long size = 100, DateTime? modified = null)
        => new(path, size, Modified, modified ?? Modified, null);

    [Fact]
    public void Keep_ThenIsKept_True_AndSurvivesReloadFromDisk()
    {
        new KeptStore(StorePath).Keep(File());

        Assert.True(new KeptStore(StorePath).IsKept(File()));
    }

    [Fact]
    public void IsKept_ComparesPathsCaseInsensitively()
    {
        new KeptStore(StorePath).Keep(File(@"C:\d\Setup.exe"));

        Assert.True(new KeptStore(StorePath).IsKept(File(@"c:\D\setup.EXE")));
    }

    [Fact]
    public void FileModifiedAfterKeep_IsNoLongerKept()
    {
        var store = new KeptStore(StorePath);
        store.Keep(File());

        Assert.False(store.IsKept(File(modified: Modified.AddSeconds(1))));
    }

    [Fact]
    public void FileWithDifferentSize_IsNoLongerKept()
    {
        var store = new KeptStore(StorePath);
        store.Keep(File(size: 100));

        Assert.False(store.IsKept(File(size: 101)));
    }

    [Fact]
    public void Unkeep_RemovesEntry_AndReturnsFalseWhenMissing()
    {
        var store = new KeptStore(StorePath);
        store.Keep(File());

        Assert.True(store.Unkeep(@"C:\d\setup.exe"));
        Assert.False(store.IsKept(File()));
        Assert.False(store.Unkeep(@"C:\d\setup.exe"));
    }

    [Fact]
    public void Rename_KeepsFileKeptAtNewPath()
    {
        var store = new KeptStore(StorePath);
        store.Keep(File(@"C:\d\setup.exe"));

        store.Rename(@"C:\d\setup.exe", @"C:\d\安装包\setup.exe");

        var reloaded = new KeptStore(StorePath);
        Assert.True(reloaded.IsKept(File(@"C:\d\安装包\setup.exe")));
        Assert.False(reloaded.IsKept(File(@"C:\d\setup.exe")));
    }

    [Fact]
    public void CorruptJsonFile_BehavesAsEmpty_AndNextKeepOverwritesIt()
    {
        System.IO.File.WriteAllText(StorePath, "{ this is not json");
        var store = new KeptStore(StorePath);

        Assert.Empty(store.All());

        store.Keep(File());

        Assert.True(new KeptStore(StorePath).IsKept(File()));
    }

    [Fact]
    public void All_ListsEntriesWithTheirKeys()
    {
        var store = new KeptStore(StorePath);
        store.Keep(File(size: 7));

        Assert.Equal(new KeptEntry(@"C:\d\setup.exe", 7, Modified), Assert.Single(store.All()));
    }
}
