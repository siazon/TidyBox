using System.Globalization;
using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer.Tree;

public static class DiskRowInfo
{
    /// <summary>
    /// One tab-separated line about a row, ready to paste into a spreadsheet: name, path, size in bytes, size as shown,
    /// files, folders, last modified. For the "[N files]" row the path is the folder those files are in.
    /// </summary>
    public static string ToClipboardText(DiskNode node) => string.Join('\t',
        node.Name,
        node.FullPath,
        node.SizeBytes.ToString(CultureInfo.InvariantCulture),
        ByteSizeFormatter.Format(node.SizeBytes),
        node.FileCount.ToString(CultureInfo.InvariantCulture),
        node.FolderCount.ToString(CultureInfo.InvariantCulture),
        node.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
