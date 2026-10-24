using System.Collections.Concurrent;

namespace PowerTools.Modules.DiskAnalyzer.Scanning;

public class DirectoryScanner
{
    // IgnoreInaccessible skips only the locked parts instead of aborting the whole subtree. Hidden/system files do
    // count towards disk usage (the default AttributesToSkip would drop them); reparse points (junctions, symlinks)
    // are skipped to avoid loops and double counting.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public async Task<DiskScanSnapshot> ScanAsync(string rootPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var subfolders = new DirectoryInfo(rootPath)
            .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .ToList();

        var entries = new ConcurrentBag<DiskScanEntry>();
        await Parallel.ForEachAsync(subfolders, ct, (dir, token) =>
        {
            entries.Add(Aggregate(dir, token));
            return ValueTask.CompletedTask;
        });

        var ordered = entries.OrderByDescending(e => e.SizeBytes).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
        return new DiskScanSnapshot(rootPath, DateTime.UtcNow, ordered);
    }

    private static DiskScanEntry Aggregate(DirectoryInfo dir, CancellationToken ct)
    {
        long size = 0;
        var count = 0;
        var latest = DateTime.MinValue;

        try
        {
            foreach (var file in dir.EnumerateFiles("*", Options))
            {
                ct.ThrowIfCancellationRequested();
                size += file.Length;
                count++;
                if (file.LastWriteTimeUtc > latest) latest = file.LastWriteTimeUtc;
            }
        }
        catch (IOException)
        {
            // A file or folder vanished or became unreadable mid-scan; keep what was counted so far.
        }

        return new DiskScanEntry(dir.FullName, size, count == 0 ? dir.LastWriteTimeUtc : latest, count);
    }
}
