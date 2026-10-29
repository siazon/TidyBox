using TidyBox.Core.Recycling;
using TidyBox.Modules.Downloads.Journal;
using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads.Tests.Tidy;

public class TidyExecutorTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _base = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());
    private readonly string _root;
    private readonly string _store;
    private readonly FakeRecycler _recycler = new();

    public TidyExecutorTests()
    {
        _root = Path.Combine(_base, "Downloads");
        _store = Path.Combine(_base, "store");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_store);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private sealed class FakeRecycler : IFileRecycler
    {
        public List<string> Deleted { get; } = new();

        public string? ThrowFor { get; set; }

        public string? CancelFor { get; set; }

        public void Delete(string filePath)
        {
            if (filePath == ThrowFor) throw new IOException("boom");
            if (filePath == CancelFor) throw new OperationCanceledException(); // what the shell does when the user declines its prompt
            Deleted.Add(filePath);
        }
    }

    private JournalStore Journal => new(Path.Combine(_store, "journal"));

    private KeptStore Kept => new(Path.Combine(_store, "kept.json"));

    private TidyExecutor NewExecutor() => new(_root, Journal, Kept, _recycler, () => Now);

    private DownloadFile Source(string name, string content = "hello", string? directory = null)
    {
        var path = Path.Combine(directory ?? _root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, Now - TimeSpan.FromDays(2)); // old enough that nothing counts as "just written"
        var info = new FileInfo(path);
        return new DownloadFile(path, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, null);
    }

    private Proposal Move(DownloadFile file, string? destination = null)
        => new(file, ProposalKind.Move, "documents", destination ?? Path.Combine(_root, "文档", file.Name), "移动到「文档」", true, false);

    private static Proposal Duplicate(DownloadFile file, string existing)
        => new(file, ProposalKind.RecycleDuplicate, "documents", existing, "目标已有内容相同的文件", false, false);

    [Fact]
    public void Execute_MovesFile_CreatesCategoryFolder_AndRecordsMoveBatch()
    {
        var file = Source("a.pdf");
        var destination = Path.Combine(_root, "文档", "a.pdf");

        var result = NewExecutor().Execute(new[] { Move(file) });

        Assert.False(File.Exists(file.FullPath));
        Assert.True(File.Exists(destination));
        var batch = result.MoveBatch!;
        var entry = Assert.Single(batch.Entries);
        Assert.Equal((BatchKind.Move, file.FullPath, destination, file.SizeBytes, file.ModifiedUtc), (batch.Kind, entry.From, entry.To, entry.SizeBytes, entry.ModifiedUtc));
        Assert.Equal(batch.BatchId, Assert.Single(Journal.LoadAll()).BatchId);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Execute_MovePreservesTimestampsAndZoneIdentifier()
    {
        var file = Source("a.pdf");
        File.WriteAllText(file.FullPath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var modified = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(file.FullPath, created);
        File.SetLastWriteTimeUtc(file.FullPath, modified);
        var current = file with { CreatedUtc = created, ModifiedUtc = modified };

        NewExecutor().Execute(new[] { Move(current) });

        var destination = Path.Combine(_root, "文档", "a.pdf");
        Assert.Equal((created, modified), (File.GetCreationTimeUtc(destination), File.GetLastWriteTimeUtc(destination)));
        Assert.Contains("ZoneId=3", File.ReadAllText(destination + ":Zone.Identifier"));
    }

    [Fact]
    public void Execute_MoveAndRecycleDuplicate_ProduceTwoSeparateBatches()
    {
        var moved = Source("a.pdf");
        var copy = Source("b.pdf", "same");
        var existing = Source("b.pdf", "same", Path.Combine(_root, "文档")).FullPath;

        var result = NewExecutor().Execute(new[] { Move(moved), Duplicate(copy, existing) });

        Assert.Equal((BatchKind.Move, BatchKind.Recycle), (result.MoveBatch!.Kind, result.RecycleBatch!.Kind));
        Assert.Equal(new[] { copy.FullPath }, _recycler.Deleted);
        Assert.Equal(2, Journal.LoadAll().Count);
    }

    [Fact]
    public void Execute_NeverOverwrites_WhenDestinationAppearedAfterPlanning()
    {
        var file = Source("a.pdf", "mine");
        var destination = Path.Combine(_root, "文档", "a.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "someone else's");

        var result = NewExecutor().Execute(new[] { Move(file, destination) });

        Assert.Equal(file.FullPath, Assert.Single(result.Failures).Path);
        Assert.Equal("mine", File.ReadAllText(file.FullPath));
        Assert.Equal("someone else's", File.ReadAllText(destination));
        Assert.Null(result.MoveBatch);
    }

    [Fact]
    public void Execute_SourceVanished_ReportsFailure_AndContinuesWithOthers()
    {
        var gone = Source("gone.pdf");
        var stays = Source("stays.pdf");
        File.Delete(gone.FullPath);

        var result = NewExecutor().Execute(new[] { Move(gone), Move(stays) });

        Assert.Equal(gone.FullPath, Assert.Single(result.Failures).Path);
        Assert.Equal(stays.FullPath, Assert.Single(result.MoveBatch!.Entries).From);
    }

    [Fact]
    public void Execute_IgnoresUntouchedProposals()
    {
        var file = Source("notes.md");

        var result = NewExecutor().Execute(new[] { new Proposal(file, ProposalKind.Untouched, null, null, "没有匹配的规则，留在原处", false, false) });

        Assert.True(File.Exists(file.FullPath));
        Assert.Equal((null, null), (result.MoveBatch, result.RecycleBatch));
        Assert.Empty(Journal.LoadAll());
    }

    [Fact]
    public void Execute_RenamesKeptEntryToNewPath()
    {
        var file = Source("a.pdf");
        Kept.Keep(file);

        NewExecutor().Execute(new[] { Move(file) });

        var moved = file with { FullPath = Path.Combine(_root, "文档", "a.pdf") };
        Assert.True(Kept.IsKept(moved));
    }

    [Fact]
    public void Execute_AnyPathOutsideDownloadsRoot_ThrowsArgumentException_AndMovesNothing()
    {
        var inside = Source("a.pdf");
        var outside = Source("evil.pdf", directory: Path.Combine(_base, "elsewhere"));

        Assert.Throws<ArgumentException>(() => NewExecutor().Execute(new[] { Move(inside), Move(outside) }));
        Assert.True(File.Exists(inside.FullPath));
        Assert.True(File.Exists(outside.FullPath));
    }

    [Fact]
    public void Execute_DestinationOutsideDownloadsRoot_Throws_AndMovesNothing()
    {
        var inside = Source("a.pdf");
        var escape = Path.Combine(_root, "..", "escaped.pdf");

        Assert.Throws<ArgumentException>(() => NewExecutor().Execute(new[] { Move(inside, escape) }));
        Assert.True(File.Exists(inside.FullPath));
    }

    [Fact]
    public void Execute_CategoryFolderIsAJunction_ReportsFailure_AndMovesNothing()
    {
        var file = Source("a.pdf");
        var elsewhere = Path.Combine(_base, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        if (!TestLinks.TryCreateDirectoryLink(Path.Combine(_root, "文档"), elsewhere)) return;

        var result = NewExecutor().Execute(new[] { Move(file) });

        Assert.Single(result.Failures);
        Assert.True(File.Exists(file.FullPath));
        Assert.Empty(Directory.GetFiles(elsewhere));
    }

    [Fact]
    public void Recycle_RecordsRecycleBatch_AndCallsRecyclerOnlyForGivenFiles()
    {
        var old = Source("old.exe");
        Source("other.exe");

        var result = NewExecutor().Recycle(new[] { old });

        Assert.Equal(new[] { old.FullPath }, _recycler.Deleted);
        Assert.Equal((BatchKind.Recycle, old.FullPath, null), (result.RecycleBatch!.Kind, result.RecycleBatch.Entries[0].From, result.RecycleBatch.Entries[0].To));
        Assert.Null(result.MoveBatch);
    }

    [Fact]
    public void Recycle_PathOutsideRoot_Throws_AndRecyclesNothing()
    {
        var inside = Source("a.exe");
        var outside = Source("b.exe", directory: Path.Combine(_base, "elsewhere"));

        Assert.Throws<ArgumentException>(() => NewExecutor().Recycle(new[] { inside, outside }));
        Assert.Empty(_recycler.Deleted);
    }

    [Fact]
    public void Execute_KeptStoreCannotBeSaved_StillJournalsTheCompletedMoves()
    {
        var file = Source("a.pdf");
        var kept = Kept;
        kept.Keep(file);
        var keptPath = Path.Combine(_store, "kept.json");
        File.Delete(keptPath);
        Directory.CreateDirectory(keptPath); // from here on, writing kept.json fails
        var executor = new TidyExecutor(_root, Journal, kept, _recycler, () => Now);

        var result = executor.Execute(new[] { Move(file) });

        Assert.Equal(file.FullPath, Assert.Single(result.MoveBatch!.Entries).From);
        Assert.Equal(result.MoveBatch.BatchId, Assert.Single(Journal.LoadAll()).BatchId);
    }

    [Fact]
    public void Recycle_RecyclerCancelled_ReportsFailure_AndKeepsWhatWasRecycled()
    {
        var declined = Source("declined.exe");
        var good = Source("good.exe");
        _recycler.CancelFor = declined.FullPath;

        var result = NewExecutor().Recycle(new[] { declined, good });

        Assert.Equal(declined.FullPath, Assert.Single(result.Failures).Path);
        Assert.Equal(good.FullPath, Assert.Single(result.RecycleBatch!.Entries).From);
    }

    [Fact]
    public void Execute_RecycleDuplicate_IdenticalFileIsGone_DoesNotRecycle()
    {
        var copy = Source("b.pdf", "same");
        var existing = Source("b.pdf", "same", Path.Combine(_root, "文档")).FullPath;
        File.Delete(existing); // the user removed the other copy while the page was open

        var result = NewExecutor().Execute(new[] { Duplicate(copy, existing) });

        Assert.Empty(_recycler.Deleted);
        Assert.Equal(copy.FullPath, Assert.Single(result.Failures).Path);
        Assert.True(File.Exists(copy.FullPath));
    }

    [Fact]
    public void Execute_RecycleDuplicate_OtherCopyNowDiffers_DoesNotRecycle()
    {
        var copy = Source("b.pdf", "same");
        var existing = Source("b.pdf", "same", Path.Combine(_root, "文档")).FullPath;
        File.WriteAllText(existing, "edited since");

        var result = NewExecutor().Execute(new[] { Duplicate(copy, existing) });

        Assert.Empty(_recycler.Deleted);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Recycle_FileChangedSinceScan_IsNotRecycled()
    {
        var grown = Source("grown.exe");
        var retimed = Source("retimed.exe");
        File.AppendAllText(grown.FullPath, "more");                                          // size changed
        File.SetLastWriteTimeUtc(retimed.FullPath, Now - TimeSpan.FromDays(5));            // only the time changed

        var result = NewExecutor().Recycle(new[] { grown, retimed });

        Assert.Empty(_recycler.Deleted);
        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public void Recycle_FileWrittenMinutesAgo_IsNotRecycled()
    {
        var fresh = Source("fresh.exe");
        var justNow = Now - TimeSpan.FromMinutes(1);
        File.SetLastWriteTimeUtc(fresh.FullPath, justNow);

        var result = NewExecutor().Recycle(new[] { fresh with { ModifiedUtc = justNow } });

        Assert.Empty(_recycler.Deleted);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Recycle_FileHeldOpenByAnotherProgram_IsNotRecycled()
    {
        var busy = Source("busy.exe");
        using var held = new FileStream(busy.FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = NewExecutor().Recycle(new[] { busy });

        Assert.Empty(_recycler.Deleted);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Recycle_RecyclerThrows_ReportsFailure_AndContinuesWithOthers()
    {
        var bad = Source("bad.exe");
        var good = Source("good.exe");
        _recycler.ThrowFor = bad.FullPath;

        var result = NewExecutor().Recycle(new[] { bad, good });

        Assert.Equal(bad.FullPath, Assert.Single(result.Failures).Path);
        Assert.Equal(good.FullPath, Assert.Single(result.RecycleBatch!.Entries).From);
    }
}
