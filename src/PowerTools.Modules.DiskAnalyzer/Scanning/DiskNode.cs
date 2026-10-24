namespace PowerTools.Modules.DiskAnalyzer.Scanning;

public enum DiskNodeKind
{
    Folder,

    /// <summary>The files directly inside a folder, shown as one "[N 个文件]" row.</summary>
    FilesGroup,
}

/// <summary>
/// One row of the scanned directory tree. For a folder, every figure covers its whole subtree. Children are the
/// sub-folders plus, when the folder has files of its own, one <see cref="DiskNodeKind.FilesGroup"/> node,
/// ordered by size, largest first.
/// </summary>
/// <param name="FolderCount">Folders below this node (not counting the node itself).</param>
/// <param name="LastModifiedUtc">Newest file in the subtree; the folder's own time when it contains no files.</param>
public sealed record DiskNode(
    string Name,
    string FullPath,
    DiskNodeKind Kind,
    long SizeBytes,
    int FileCount,
    int FolderCount,
    DateTime LastModifiedUtc,
    IReadOnlyList<DiskNode> Children);
