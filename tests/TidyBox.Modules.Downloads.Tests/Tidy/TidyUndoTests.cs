using TidyBox.Core.Recycling;
using TidyBox.Modules.Downloads.Journal;
using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads.Tests.Tidy;

public class TidyUndoTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _base = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());
    private readonly string _root;
    private readonly string _store;

    public TidyUndoTests()
    {
        _root = Path.Combine(_base, "Downloads");
        _store = Path.Combine(_base, "store");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_store);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private sealed class NoRecycler : IFileRecycler
    {
        public void Delete(string filePath) { }
    }

    private JournalStore Journal => new(Path.Combine(_store, "journal"));

    private KeptStore Kept => new(Path.Combine(_store, "kept.json"));

    private TidyUndo NewUndo() => new(_root, Journal, Kept);

    private DownloadFile Source(string name, string content = "hello")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        var info = new FileInfo(path);
        return new DownloadFile(path, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, null);
    }

    private string Dest(string name) => Path.Combine(_root, "文档", name);

    private Batch TidyAway(params DownloadFile[] files)
    {
        var proposals = files.Select(f => new Proposal(f, ProposalKind.Move, "documents", Dest(f.Name), "移动到「文档」", true, false)).ToList();
        var executor = new TidyExecutor(_root, Journal, Kept, new NoRecycler(), () => Now);
        return executor.Execute(proposals).MoveBatch!;
    }

    [Fact]
    public void Undo_RestoresFilesToOriginalPaths_AndDeletesEmptyBatch()
    {
        var file = Source("a.pdf", "mine");
        var batch = TidyAway(file);

        var result = NewUndo().Undo(batch.BatchId);

        Assert.Equal((1, 0), (result.Restored, result.Skipped.Count));
        Assert.Equal("mine", File.ReadAllText(file.FullPath));
        Assert.False(File.Exists(Dest("a.pdf")));
        Assert.Empty(Journal.LoadAll());
    }

    [Fact]
    public void Undo_RestoresTimestamps_Unchanged()
    {
        var file = Source("a.pdf");
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var modified = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(file.FullPath, created);
        File.SetLastWriteTimeUtc(file.FullPath, modified);
        var batch = TidyAway(file with { CreatedUtc = created, ModifiedUtc = modified });

        NewUndo().Undo(batch.BatchId);

        Assert.Equal((created, modified), (File.GetCreationTimeUtc(file.FullPath), File.GetLastWriteTimeUtc(file.FullPath)));
    }

    [Fact]
    public void Undo_TargetDeletedByUser_SkipsWithTargetMissing_OthersStillRestored_AndOnlySkippedStayInBatch()
    {
        var gone = Source("gone.pdf");
        var stays = Source("stays.pdf");
        var batch = TidyAway(gone, stays);
        File.Delete(Dest("gone.pdf"));

        var result = NewUndo().Undo(batch.BatchId);

        var skip = Assert.Single(result.Skipped);
        Assert.Equal((1, UndoSkipReason.TargetMissing, Dest("gone.pdf")), (result.Restored, skip.Reason, skip.Entry.To));
        Assert.True(File.Exists(stays.FullPath));
        var remaining = Assert.Single(Journal.LoadAll());
        Assert.Equal(Dest("gone.pdf"), Assert.Single(remaining.Entries).To);
    }

    [Fact]
    public void Undo_TargetModifiedSinceMove_SkipsWithTargetChanged()
    {
        var biggerNow = Source("bigger.pdf", "abc");
        var touched = Source("touched.pdf", "abc");
        var batch = TidyAway(biggerNow, touched);
        File.WriteAllText(Dest("bigger.pdf"), "abcdef");                                   // size changed
        File.SetLastWriteTimeUtc(Dest("touched.pdf"), new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // only the time changed

        var result = NewUndo().Undo(batch.BatchId);

        Assert.Equal(0, result.Restored);
        Assert.All(result.Skipped, skip => Assert.Equal(UndoSkipReason.TargetChanged, skip.Reason));
        Assert.Equal(2, result.Skipped.Count);
    }

    [Fact]
    public void Undo_OriginalPathNowOccupied_SkipsWithOriginalOccupied_AndDoesNotOverwrite()
    {
        var file = Source("a.pdf", "mine");
        var batch = TidyAway(file);
        File.WriteAllText(file.FullPath, "a newer file that took the name");

        var result = NewUndo().Undo(batch.BatchId);

        Assert.Equal(UndoSkipReason.OriginalOccupied, Assert.Single(result.Skipped).Reason);
        Assert.Equal("a newer file that took the name", File.ReadAllText(file.FullPath));
        Assert.Equal("mine", File.ReadAllText(Dest("a.pdf")));
    }

    [Fact]
    public void Undo_RenamesKeptEntryBackToOriginalPath()
    {
        var file = Source("a.pdf");
        Kept.Keep(file);
        var batch = TidyAway(file);

        NewUndo().Undo(batch.BatchId);

        Assert.True(Kept.IsKept(file));
    }

    [Fact]
    public void Undo_RecycleBatch_Throws()
    {
        Journal.Save(new Batch("r1", BatchKind.Recycle, Now, new[] { new BatchEntry(Path.Combine(_root, "x.exe"), null, 1, Now, "移到回收站") }));

        Assert.Throws<InvalidOperationException>(() => NewUndo().Undo("r1"));
    }

    [Fact]
    public void Undo_UnknownBatchId_Throws() => Assert.Throws<InvalidOperationException>(() => NewUndo().Undo("nope"));

    [Fact]
    public void Undo_JournalEntryOutsideRoot_Throws_AndRestoresNothing()
    {
        var file = Source("a.pdf");
        var batch = TidyAway(file);
        var tampered = batch with
        {
            Entries = batch.Entries.Append(new BatchEntry(Path.Combine(_base, "elsewhere", "evil.pdf"), Dest("a.pdf"), 1, Now, "x")).ToList(),
        };
        Journal.Save(tampered);

        Assert.Throws<ArgumentException>(() => NewUndo().Undo(batch.BatchId));
        Assert.True(File.Exists(Dest("a.pdf")));
        Assert.False(File.Exists(file.FullPath));
    }
}
