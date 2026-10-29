using TidyBox.Core.Recycling;
using TidyBox.Modules.Downloads.Journal;
using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tidy;

public sealed record Failure(string Path, string Message);

/// <param name="MoveBatch">What was moved, or null when nothing was.</param>
/// <param name="RecycleBatch">What was sent to the Recycle Bin, or null when nothing was.</param>
public sealed record ExecutionResult(Batch? MoveBatch, Batch? RecycleBatch, IReadOnlyList<Failure> Failures);

/// <summary>
/// Carries out what the user approved. Every path is checked to be inside the downloads folder before anything is touched, a move
/// never replaces an existing file, and each outcome is written to the journal.
/// </summary>
public sealed class TidyExecutor
{
    private readonly string _downloadsRoot;
    private readonly JournalStore _journal;
    private readonly KeptStore _kept;
    private readonly IFileRecycler _recycler;
    private readonly Func<DateTime> _clock;

    public TidyExecutor(string downloadsRoot, JournalStore journal, KeptStore kept, IFileRecycler recycler, Func<DateTime> clock)
    {
        _downloadsRoot = downloadsRoot;
        _journal = journal;
        _kept = kept;
        _recycler = recycler;
        _clock = clock;
    }

    /// <exception cref="ArgumentException">A path is outside the downloads folder; nothing was changed.</exception>
    public ExecutionResult Execute(IReadOnlyList<Proposal> selected)
    {
        foreach (var proposal in selected.Where(p => p.Kind != ProposalKind.Untouched))
        {
            RequireInsideRoot(proposal.File.FullPath);
            if (proposal.DestinationPath is not null) RequireInsideRoot(proposal.DestinationPath);
        }

        var failures = new List<Failure>();
        var moves = new List<BatchEntry>();
        Batch? moveBatch;
        try
        {
            foreach (var proposal in selected.Where(p => p.Kind == ProposalKind.Move))
            {
                if (MoveOne(proposal, failures) is { } entry) moves.Add(entry);
            }
        }
        finally
        {
            // Whatever goes wrong part-way, the files already moved are journaled, so they can still be undone.
            moveBatch = Record(BatchKind.Move, moves);
        }

        var duplicates = selected.Where(p => p.Kind == ProposalKind.RecycleDuplicate).Select(p => (p.File, p.Reason, IdenticalCopy: p.DestinationPath));
        return new ExecutionResult(moveBatch, RecycleAll(duplicates, failures), failures);
    }

    /// <exception cref="ArgumentException">A path is outside the downloads folder; nothing was changed.</exception>
    public ExecutionResult Recycle(IReadOnlyList<DownloadFile> files)
    {
        foreach (var file in files) RequireInsideRoot(file.FullPath);

        var failures = new List<Failure>();
        var batch = RecycleAll(files.Select(file => (file, "移到回收站", (string?)null)), failures);
        return new ExecutionResult(null, batch, failures);
    }

    private BatchEntry? MoveOne(Proposal proposal, List<Failure> failures)
    {
        var source = proposal.File.FullPath;
        var destination = proposal.DestinationPath!;
        var folder = Path.GetDirectoryName(destination)!;

        // A category folder that is a link could lead out of the downloads folder.
        if (Directory.Exists(folder) && new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            failures.Add(new Failure(source, $"目标文件夹「{Path.GetFileName(folder)}」是一个链接，未移动"));
            return null;
        }

        if (!File.Exists(source))
        {
            failures.Add(new Failure(source, "文件已不存在"));
            return null;
        }

        if (Path.Exists(destination))
        {
            failures.Add(new Failure(source, "目标位置已有同名文件，未覆盖"));
            return null;
        }

        try
        {
            Directory.CreateDirectory(folder);
            File.Move(source, destination); // does not overwrite: it throws if the destination appeared in the meantime
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new Failure(source, ex.Message));
            return null;
        }

        try
        {
            _kept.Rename(source, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is moved and will be journaled; the only cost of a kept list that could not be saved is that the file may show up in the review again.
        }

        var moved = new FileInfo(destination);
        return new BatchEntry(source, destination, moved.Length, moved.LastWriteTimeUtc, proposal.Reason);
    }

    // Recycling is the more destructive action, so each file is checked again right before it goes: what was true at scan time may not be now.
    private Batch? RecycleAll(IEnumerable<(DownloadFile File, string Reason, string? IdenticalCopy)> files, List<Failure> failures)
    {
        var entries = new List<BatchEntry>();
        foreach (var (file, reason, identicalCopy) in files)
        {
            string? problem;
            try
            {
                problem = ReasonNotToRecycle(file, identicalCopy);
                if (problem is null) _recycler.Delete(file.FullPath);
            }
            catch (OperationCanceledException)
            {
                problem = "已取消，没有移到回收站";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = ex.Message;
            }

            if (problem is not null)
            {
                failures.Add(new Failure(file.FullPath, problem));
                continue;
            }

            entries.Add(new BatchEntry(file.FullPath, null, file.SizeBytes, file.ModifiedUtc, reason));
        }

        return Record(BatchKind.Recycle, entries);
    }

    private string? ReasonNotToRecycle(DownloadFile file, string? identicalCopy)
    {
        var info = new FileInfo(file.FullPath);
        if (!info.Exists) return "文件已不存在";
        if (info.Length != file.SizeBytes || info.LastWriteTimeUtc.Ticks != file.ModifiedUtc.Ticks) return "文件在扫描之后被改动过，请重新扫描";
        if (!SafetyFilter.IsEligible(info, _clock())) return "文件正在被使用或刚刚写入，稍后再试";
        if (identicalCopy is not null && !ContentCompare.AreSame(file.FullPath, identicalCopy)) return "目标处内容相同的那一份已不在或已改变，没有回收";
        return null;
    }

    private Batch? Record(BatchKind kind, List<BatchEntry> entries)
    {
        if (entries.Count == 0) return null;

        var batch = new Batch(Guid.NewGuid().ToString("N"), kind, _clock(), entries);
        _journal.Save(batch);
        return batch;
    }

    private void RequireInsideRoot(string path) => PathGuard.RequireInside(_downloadsRoot, path);
}
