using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.App.Views;

/// <summary>One candidate in the orphan-folder review list: unchecked by default, user confirms before anything moves.</summary>
public sealed class OrphanFolderRow
{
    public OrphanFolderRow(OrphanGameFolder folder) => Folder = folder;

    public OrphanGameFolder Folder { get; }

    public bool IsChecked { get; set; }

    public string Name => Folder.FolderName;

    public string Detail => $"{ByteSizeFormatter.Format(Folder.SizeBytes)} · 最后修改 {Folder.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd} · {Folder.LibraryPath}";
}
