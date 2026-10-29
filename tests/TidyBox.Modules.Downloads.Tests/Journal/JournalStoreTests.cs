using TidyBox.Modules.Downloads.Journal;

namespace TidyBox.Modules.Downloads.Tests.Journal;

public class JournalStoreTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public JournalStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Batch MakeBatch(string id, DateTime created, BatchKind kind = BatchKind.Move)
        => new(id, kind, created, new[] { new BatchEntry(@"C:\d\a.pdf", @"C:\d\文档\a.pdf", 12, T0, "移动到「文档」") });

    [Fact]
    public void SaveThenLoadAll_RoundTripsBatch()
    {
        var store = new JournalStore(_dir);
        store.Save(MakeBatch("b1", T0));

        var loaded = Assert.Single(new JournalStore(_dir).LoadAll());
        var entry = Assert.Single(loaded.Entries);

        Assert.Equal(("b1", BatchKind.Move, T0), (loaded.BatchId, loaded.Kind, loaded.CreatedUtc));
        Assert.Equal(new BatchEntry(@"C:\d\a.pdf", @"C:\d\文档\a.pdf", 12, T0, "移动到「文档」"), entry);
    }

    [Fact]
    public void LoadAll_IsNewestFirst()
    {
        var store = new JournalStore(_dir);
        store.Save(MakeBatch("old", T0));
        store.Save(MakeBatch("new", T0.AddHours(1), BatchKind.Recycle));

        Assert.Equal(new[] { "new", "old" }, store.LoadAll().Select(b => b.BatchId));
    }

    [Fact]
    public void LoadAll_SkipsCorruptFile_AndKeepsTheRest()
    {
        var store = new JournalStore(_dir);
        store.Save(MakeBatch("good", T0));
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not json");

        Assert.Equal("good", Assert.Single(store.LoadAll()).BatchId);
    }

    [Fact]
    public void Delete_RemovesTheBatchFile()
    {
        var store = new JournalStore(_dir);
        store.Save(MakeBatch("b1", T0));

        store.Delete("b1");

        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void LoadAll_MissingDirectory_IsEmpty() => Assert.Empty(new JournalStore(Path.Combine(_dir, "nope")).LoadAll());

    [Fact]
    public void BatchIdWithPathSeparators_IsRejected()
    {
        var store = new JournalStore(_dir);

        Assert.Throws<ArgumentException>(() => store.Save(MakeBatch(@"..\evil", T0)));
        Assert.Throws<ArgumentException>(() => store.Delete(@"..\evil"));
    }
}
