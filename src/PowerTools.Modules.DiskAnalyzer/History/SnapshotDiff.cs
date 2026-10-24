using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer.History;

public record DiskScanEntryDelta(string Path, long SizeDeltaBytes, long PreviousSizeBytes, long CurrentSizeBytes);

public static class SnapshotDiff
{
    /// <summary>
    /// Compares two snapshots by folder path, largest change (growth or shrinkage) first. A folder missing from
    /// one side counts as size 0 there, so deleted folders show as shrinkage and new folders as growth.
    /// </summary>
    public static IReadOnlyList<DiskScanEntryDelta> Compare(DiskScanSnapshot previous, DiskScanSnapshot current)
    {
        var before = previous.Entries.ToDictionary(e => e.Path, e => e.SizeBytes, StringComparer.OrdinalIgnoreCase);
        var after = current.Entries.ToDictionary(e => e.Path, e => e.SizeBytes, StringComparer.OrdinalIgnoreCase);

        return before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                before.TryGetValue(path, out var previousSize);
                after.TryGetValue(path, out var currentSize);
                return new DiskScanEntryDelta(path, currentSize - previousSize, previousSize, currentSize);
            })
            .OrderByDescending(d => Math.Abs(d.SizeDeltaBytes))
            .ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
