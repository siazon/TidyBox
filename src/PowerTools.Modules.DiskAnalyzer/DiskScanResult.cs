using PowerTools.Modules.DiskAnalyzer.History;
using PowerTools.Modules.DiskAnalyzer.Scanning;
using PowerTools.Modules.DiskAnalyzer.Tree;

namespace PowerTools.Modules.DiskAnalyzer;

/// <param name="Current">The flat, top-level snapshot that is stored as history.</param>
/// <param name="Previous">The scan stored before this one; null the first time a folder is scanned.</param>
/// <param name="Changes">Current vs previous, biggest change first; empty when there is no previous scan.</param>
/// <param name="Root">The full directory tree of this scan (kept in memory only, not stored).</param>
public record DiskScanResult(
    DiskScanSnapshot Current,
    DiskScanSnapshot? Previous,
    IReadOnlyList<DiskScanEntryDelta> Changes,
    DiskNode Root)
{
    public DiskTreeModel CreateTreeModel() => new(Root, Changes);
}
