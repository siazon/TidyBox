using System.Collections.Concurrent;

namespace PowerTools.Modules.DiskAnalyzer.Scanning;

public class DirectoryScanner
{
    // IgnoreInaccessible skips only the locked parts instead of aborting the whole subtree. Hidden/system entries
    // do count towards disk usage (the default AttributesToSkip would drop them); reparse points (junctions,
    // symlinks) are skipped to avoid loops and double counting.
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>The flat view used for history: one entry per direct sub-folder of the root.</summary>
    public async Task<DiskScanSnapshot> ScanAsync(string rootPath, CancellationToken ct = default)
    {
        return ToSnapshot(await ScanTreeAsync(rootPath, ct));
    }

    /// <summary>Projects a scanned tree onto its direct sub-folders; files directly in the root are not entries.</summary>
    public static DiskScanSnapshot ToSnapshot(DiskNode root)
    {
        var entries = root.Children
            .Where(node => node.Kind == DiskNodeKind.Folder)
            .Select(node => new DiskScanEntry(node.FullPath, node.SizeBytes, node.LastModifiedUtc, node.FileCount))
            .ToList();
        return new DiskScanSnapshot(root.FullPath, DateTime.UtcNow, entries);
    }

    public async Task<DiskNode> ScanTreeAsync(string rootPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var root = new DirectoryInfo(rootPath);
        var topLevel = new ConcurrentBag<DiskNode>();
        await Parallel.ForEachAsync(SafeDirectories(root), ct, (dir, token) =>
        {
            topLevel.Add(BuildFolder(dir, token));
            return ValueTask.CompletedTask;
        });

        return Assemble(rootPath, rootPath, root, topLevel);
    }

    private static DiskNode BuildFolder(DirectoryInfo dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var folders = new List<DiskNode>();
        foreach (var sub in SafeDirectories(dir)) folders.Add(BuildFolder(sub, ct));

        return Assemble(dir.Name, dir.FullName, dir, folders);
    }

    private static DiskNode Assemble(string name, string fullPath, DirectoryInfo dir, IEnumerable<DiskNode> folders)
    {
        long fileSize = 0;
        var fileCount = 0;
        var newestFile = DateTime.MinValue;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", Options))
            {
                fileSize += file.Length;
                fileCount++;
                if (file.LastWriteTimeUtc > newestFile) newestFile = file.LastWriteTimeUtc;
            }
        }
        catch (IOException)
        {
            // A file vanished or became unreadable mid-scan; keep what was counted so far.
        }

        var children = new List<DiskNode>(folders);
        if (fileCount > 0)
        {
            children.Add(new DiskNode($"[{fileCount:N0} 个文件]", fullPath, DiskNodeKind.FilesGroup, fileSize, fileCount, 0, newestFile, Array.Empty<DiskNode>()));
        }

        children.Sort((a, b) =>
        {
            var bySize = b.SizeBytes.CompareTo(a.SizeBytes);
            return bySize != 0 ? bySize : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        var totalFiles = fileCount;
        var totalFolders = 0;
        var totalSize = fileSize;
        var newest = newestFile;
        foreach (var folder in children.Where(c => c.Kind == DiskNodeKind.Folder))
        {
            totalFiles += folder.FileCount;
            totalFolders += 1 + folder.FolderCount;
            totalSize += folder.SizeBytes;
            // An empty sub-folder only has its own timestamp, which says nothing about the files below us.
            if (folder.FileCount > 0 && folder.LastModifiedUtc > newest) newest = folder.LastModifiedUtc;
        }

        return new DiskNode(name, fullPath, DiskNodeKind.Folder, totalSize, totalFiles, totalFolders,
            totalFiles == 0 ? dir.LastWriteTimeUtc : newest, children);
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateDirectories("*", Options).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<DirectoryInfo>();
        }
    }
}
