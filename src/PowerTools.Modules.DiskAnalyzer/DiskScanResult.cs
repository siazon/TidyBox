using PowerTools.Modules.DiskAnalyzer.History;
using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer;

/// <param name="Previous">The scan stored before this one; null the first time a folder is scanned.</param>
/// <param name="Changes">Current vs previous, biggest change first; empty when there is no previous scan.</param>
public record DiskScanResult(DiskScanSnapshot Current, DiskScanSnapshot? Previous, IReadOnlyList<DiskScanEntryDelta> Changes);
