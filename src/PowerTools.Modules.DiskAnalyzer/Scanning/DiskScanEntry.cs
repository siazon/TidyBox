namespace PowerTools.Modules.DiskAnalyzer.Scanning;

/// <summary>One direct subfolder of the scan root, aggregated over its whole subtree.</summary>
public record DiskScanEntry(string Path, long SizeBytes, DateTime LastModifiedUtc, int ItemCount);
