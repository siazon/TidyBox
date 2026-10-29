using TidyBox.Modules.Downloads.Journal;
using TidyBox.Modules.Downloads.Review;

namespace TidyBox.Modules.Downloads.Tidy;

public enum UndoSkipReason
{
    /// <summary>The moved file is no longer where TidyBox put it.</summary>
    TargetMissing,

    /// <summary>The moved file has been changed since (its size or modified time differs from what was recorded).</summary>
    TargetChanged,

    /// <summary>Something else now has the original name.</summary>
    OriginalOccupied,

    /// <summary>The move back failed for another reason (for example, access was denied).</summary>
    CouldNotMove,
}

public sealed record UndoSkip(BatchEntry Entry, UndoSkipReason Reason);

public sealed record UndoResult(int Restored, IReadOnlyList<UndoSkip> Skipped);

public sealed class TidyUndo
{
    private readonly string _downloadsRoot;
    private readonly JournalStore _journal;
    private readonly KeptStore _kept;

    public TidyUndo(string downloadsRoot, JournalStore journal, KeptStore kept)
    {
        _downloadsRoot = downloadsRoot;
        _journal = journal;
        _kept = kept;
    }

    /// <summary>
    /// Moves each file of a move batch back, one by one. A file is only restored if it is still where TidyBox put it, unchanged, and its
    /// original name is free; anything else is skipped and reported, never overwritten. Restored entries leave the batch, which is deleted once empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such batch, or it is a recycle batch (those are restored from the Recycle Bin).</exception>
    /// <exception cref="ArgumentException">An entry points outside the downloads folder; nothing was restored.</exception>
    public UndoResult Undo(string batchId)
    {
        var batch = _journal.LoadAll().FirstOrDefault(b => b.BatchId == batchId)
            ?? throw new InvalidOperationException($"There is no batch '{batchId}'.");
        if (batch.Kind != BatchKind.Move) throw new InvalidOperationException("Only moves can be undone here; recycled files are restored from the Recycle Bin.");

        foreach (var entry in batch.Entries)
        {
            PathGuard.RequireInside(_downloadsRoot, entry.From);
            PathGuard.RequireInside(_downloadsRoot, entry.To ?? throw new ArgumentException("A move entry has no destination.", nameof(batchId)));
        }

        var skipped = new List<UndoSkip>();
        var restored = 0;
        foreach (var entry in batch.Entries)
        {
            if (SkipReason(entry) is { } reason)
            {
                skipped.Add(new UndoSkip(entry, reason));
            }
            else
            {
                restored++;
            }
        }

        if (skipped.Count == 0) _journal.Delete(batch.BatchId);
        else if (restored > 0) _journal.Save(batch with { Entries = skipped.Select(s => s.Entry).ToList() });

        return new UndoResult(restored, skipped);
    }

    // Restores one entry and returns null, or returns why it was left alone.
    private UndoSkipReason? SkipReason(BatchEntry entry)
    {
        var target = new FileInfo(entry.To!);
        if (!target.Exists) return UndoSkipReason.TargetMissing;
        if (target.Length != entry.SizeBytes || target.LastWriteTimeUtc.Ticks != entry.ModifiedUtc.Ticks) return UndoSkipReason.TargetChanged;
        if (Path.Exists(entry.From)) return UndoSkipReason.OriginalOccupied;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entry.From)!);
            File.Move(entry.To!, entry.From); // does not overwrite: it throws if the name was taken in the meantime
        }
        catch (IOException)
        {
            return Path.Exists(entry.From) ? UndoSkipReason.OriginalOccupied : UndoSkipReason.CouldNotMove;
        }
        catch (UnauthorizedAccessException)
        {
            return UndoSkipReason.CouldNotMove;
        }

        _kept.Rename(entry.To!, entry.From);
        return null;
    }
}
