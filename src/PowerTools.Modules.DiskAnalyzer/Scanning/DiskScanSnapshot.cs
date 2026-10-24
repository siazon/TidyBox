namespace PowerTools.Modules.DiskAnalyzer.Scanning;

public record DiskScanSnapshot(string RootPath, DateTime ScannedAtUtc, IReadOnlyList<DiskScanEntry> Entries);
