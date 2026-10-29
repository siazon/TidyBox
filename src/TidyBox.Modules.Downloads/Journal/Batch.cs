using System.Text.Json.Serialization;

namespace TidyBox.Modules.Downloads.Journal;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BatchKind
{
    /// <summary>Files moved into category folders; can be undone.</summary>
    Move,

    /// <summary>Files sent to the Recycle Bin; only the user can restore them, from the bin.</summary>
    Recycle,
}

/// <param name="To">The new path for a move; null for a recycled file.</param>
/// <param name="SizeBytes">Size and modified time as the file was after the operation, so an undo can tell whether it has changed since.</param>
public sealed record BatchEntry(string From, string? To, long SizeBytes, DateTime ModifiedUtc, string Reason);

public sealed record Batch(string BatchId, BatchKind Kind, DateTime CreatedUtc, IReadOnlyList<BatchEntry> Entries);
