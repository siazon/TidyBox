using System.Security.Cryptography;
using TidyBox.Core.Recycling;
using TidyBox.Modules.Downloads.Journal;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads.Tests;

public class DownloadsModuleWorkflowTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _base = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());
    private readonly string _root;
    private readonly string _store;
    private readonly FakeRecycler _recycler = new();
    private readonly DownloadsModule _module;

    public DownloadsModuleWorkflowTests()
    {
        _root = Path.Combine(_base, "Downloads");
        _store = Path.Combine(_base, "store");
        Directory.CreateDirectory(_root);
        _module = new DownloadsModule(_root, _store, _recycler, () => Now);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private sealed class FakeRecycler : IFileRecycler
    {
        public List<string> Deleted { get; } = new();

        public void Delete(string filePath) => Deleted.Add(filePath);
    }

    private string Make(string relative, string content, int ageDays, string? zone = null)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (zone is not null) File.WriteAllText(path + ":Zone.Identifier", zone);
        File.SetCreationTimeUtc(path, Now - TimeSpan.FromDays(ageDays));
        File.SetLastWriteTimeUtc(path, Now - TimeSpan.FromDays(ageDays));
        return path;
    }

    private Dictionary<string, (string Hash, DateTime Created, DateTime Modified)> Snapshot()
        => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(
            path => path,
            path => (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), File.GetCreationTimeUtc(path), File.GetLastWriteTimeUtc(path)),
            StringComparer.OrdinalIgnoreCase);

    private static string Zone(string url) => $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl={url}\r\n";

    [Fact]
    public async Task ScanAsync_ModifiesNothing()
    {
        Make("a.pdf", "a", 1);
        Make("old.exe", "old", 200);
        Make("notes.md", "n", 10);
        Make(Path.Combine("文档", "c.pdf"), "c", 5);
        await _module.InitializeAsync();
        var before = Snapshot();

        await _module.ScanAsync();

        Assert.Equal(before, Snapshot());
        Assert.Empty(Directory.GetFiles(Path.Combine(_store, "journal")));
        Assert.False(File.Exists(Path.Combine(_store, "kept.json")));
    }

    [Fact]
    public async Task ScanAsync_ReportsProposals_Stale_AndSourceDomains()
    {
        var a = Make("a.pdf", "aaa", 1, Zone("https://example.com/page?token=SECRET"));
        var old = Make("old.exe", "oldold", 200, Zone("https://github.com/x/y"));
        Make("notes.md", "n", 10);

        var report = await _module.ScanAsync();

        Assert.Equal(_root, report.Folder);
        Assert.Equal(3, report.Proposals.Count);
        Assert.Equal(old, Assert.Single(report.Stale).FullPath);
        Assert.Equal((long)"oldold".Length, report.StaleBytes);
        Assert.Equal((long)"aaa".Length + "oldold".Length + "n".Length, report.TotalBytes);
        Assert.Equal(("example.com", "github.com"), (report.SourceDomains[a], report.SourceDomains[old]));
        Assert.Equal(2, report.SourceDomains.Count);
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _module.ScanAsync(cancelled.Token));
    }

    [Fact]
    public async Task ApplyAsync_MovesOnlyTheSelectedProposals()
    {
        var a = Make("a.pdf", "a", 1);
        var old = Make("old.exe", "old", 200);
        var report = await _module.ScanAsync();

        await _module.ApplyAsync(report.Proposals.Where(p => p.File.FullPath == a).ToList());

        Assert.True(File.Exists(Path.Combine(_root, "文档", "a.pdf")));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public async Task ApplyThenUndo_RestoresTheFolderToItsOriginalState()
    {
        Make("a.pdf", "a", 3);
        Make("old.exe", "old", 200);
        var before = Snapshot();
        var report = await _module.ScanAsync();

        var result = await _module.ApplyAsync(report.Proposals.Where(p => p.DefaultChecked).ToList());
        var undo = _module.Undo(result.MoveBatch!.BatchId);

        Assert.Equal(2, undo.Restored);
        Assert.Equal(before, Snapshot());
        Assert.Empty(_module.GetHistory());
    }

    [Fact]
    public async Task TidiedOldInstaller_StillShowsUpInReview_WithItsOriginalAge()
    {
        Make("old.exe", "old", 200);
        var report = await _module.ScanAsync();
        await _module.ApplyAsync(report.Proposals.Where(p => p.DefaultChecked).ToList());

        var again = await _module.ScanAsync();

        var stale = Assert.Single(again.Stale);
        Assert.Equal(("安装包", 200), (stale.CategoryFolder, FileAge.AgeDays(stale, Now)));
    }

    [Fact]
    public async Task KeptFile_DisappearsFromStale_AppearsInKept_AndSurvivesBeingTidied()
    {
        Make("old.exe", "old", 200);
        var first = await _module.ScanAsync();
        _module.Keep(Assert.Single(first.Stale));

        var kept = await _module.ScanAsync();
        Assert.Empty(kept.Stale);
        Assert.Single(kept.Kept);

        await _module.ApplyAsync(kept.Proposals.Where(p => p.DefaultChecked).ToList());
        var afterTidy = await _module.ScanAsync();

        Assert.Empty(afterTidy.Stale);
        Assert.Equal("安装包", Assert.Single(afterTidy.Kept).CategoryFolder);
    }

    [Fact]
    public async Task RecycleAsync_UsesRecycler_AndAppearsInHistoryAsRecycleBatch()
    {
        var old = Make("old.exe", "old", 200);
        var report = await _module.ScanAsync();

        await _module.RecycleAsync(report.Stale);

        Assert.Equal(new[] { old }, _recycler.Deleted);
        Assert.Equal(BatchKind.Recycle, Assert.Single(_module.GetHistory()).Kind);
    }

    [Fact]
    public async Task ApplyAsync_WithProposalOutsideFolder_Throws_AndChangesNothing()
    {
        var inside = Make("a.pdf", "a", 3);
        var outsidePath = Path.Combine(_base, "elsewhere", "evil.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        File.WriteAllText(outsidePath, "x");
        var report = await _module.ScanAsync();
        var evil = new Proposal(
            new DownloadFile(outsidePath, 1, Now, Now, null), ProposalKind.Move, "documents", Path.Combine(_root, "文档", "evil.pdf"), "x", true, false);

        await Assert.ThrowsAsync<ArgumentException>(() => _module.ApplyAsync(report.Proposals.Append(evil).ToList()));

        Assert.True(File.Exists(inside));
        Assert.True(File.Exists(outsidePath));
    }
}
