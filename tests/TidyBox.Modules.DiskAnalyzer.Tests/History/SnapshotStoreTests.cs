using TidyBox.Modules.DiskAnalyzer.History;
using TidyBox.Modules.DiskAnalyzer.Scanning;

namespace TidyBox.Modules.DiskAnalyzer.Tests.History;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pt-store-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static DiskScanSnapshot Sample(string root, long size = 100)
        => new(root, new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            new[] { new DiskScanEntry(root + @"\A", size, new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), 3) });

    [Fact]
    public async Task SaveAsync_ThenLoadLatest_ReturnsSameSnapshot()
    {
        var store = new SnapshotStore(_dir);
        var snapshot = Sample(@"C:\Games");

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadLatestAsync(@"C:\Games");

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.RootPath, loaded!.RootPath);
        Assert.Equal(snapshot.ScannedAtUtc, loaded.ScannedAtUtc);
        Assert.Equal(snapshot.Entries.Single(), loaded.Entries.Single());
    }

    [Fact]
    public async Task LoadLatestAsync_UnknownRootPath_ReturnsNull()
    {
        var store = new SnapshotStore(_dir);

        Assert.Null(await store.LoadLatestAsync(@"C:\Nothing"));
    }

    [Fact]
    public async Task SaveAsync_SameRootTwice_KeepsOnlyTheLatest()
    {
        var store = new SnapshotStore(_dir);
        await store.SaveAsync(Sample(@"C:\Games", size: 100));
        await store.SaveAsync(Sample(@"C:\Games", size: 999));

        var loaded = await store.LoadLatestAsync(@"C:\Games");

        Assert.Equal(999, loaded!.Entries.Single().SizeBytes);
    }

    [Fact]
    public async Task LoadLatestAsync_RootPathCaseAndTrailingSlashDiffer_StillFindsSnapshot()
    {
        var store = new SnapshotStore(_dir);
        await store.SaveAsync(Sample(@"C:\Games"));

        Assert.NotNull(await store.LoadLatestAsync(@"c:\games\"));
    }

    [Fact]
    public async Task SaveAsync_DifferentRoots_AreStoredSeparately()
    {
        var store = new SnapshotStore(_dir);
        await store.SaveAsync(Sample(@"C:\Games", size: 1));
        await store.SaveAsync(Sample(@"D:\Data", size: 2));

        Assert.Equal(1, (await store.LoadLatestAsync(@"C:\Games"))!.Entries.Single().SizeBytes);
        Assert.Equal(2, (await store.LoadLatestAsync(@"D:\Data"))!.Entries.Single().SizeBytes);
    }

    [Fact]
    public async Task LoadLatestAsync_CorruptFile_ReturnsNullInsteadOfThrowing()
    {
        var store = new SnapshotStore(_dir);
        await store.SaveAsync(Sample(@"C:\Games"));
        foreach (var file in Directory.GetFiles(_dir)) File.WriteAllText(file, "{ not json");

        Assert.Null(await store.LoadLatestAsync(@"C:\Games"));
    }
}
