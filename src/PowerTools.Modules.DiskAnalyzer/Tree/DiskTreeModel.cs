using System.Collections.ObjectModel;
using System.Globalization;
using PowerTools.Modules.DiskAnalyzer.History;
using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer.Tree;

public enum DiskTreeColumn { Name, Size, Files, Folders, Percent, Modified }

/// <summary>
/// Turns a scanned <see cref="DiskNode"/> tree into the flat, ordered list of rows a list control shows.
/// Expanding and collapsing insert or remove rows one by one (never reset the list), so the view keeps its
/// scroll position.
/// </summary>
public sealed class DiskTreeModel
{
    private static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

    private readonly DiskNode _root;
    private readonly Dictionary<string, long> _changesByPath;
    private readonly HashSet<DiskNode> _expanded = new(ReferenceEqualityComparer.Instance);

    /// <param name="changes">Size changes since the previous scan; applied to top-level folders only.</param>
    public DiskTreeModel(DiskNode root, IReadOnlyList<DiskScanEntryDelta>? changes = null)
    {
        _root = root;
        _changesByPath = (changes ?? Array.Empty<DiskScanEntryDelta>())
            .ToDictionary(d => d.Path, d => d.SizeDeltaBytes, StringComparer.OrdinalIgnoreCase);
        _expanded.Add(root);
        Rebuild();
    }

    public ObservableCollection<DiskTreeRow> Rows { get; } = new();

    public DiskTreeColumn SortColumn { get; private set; } = DiskTreeColumn.Size;

    public bool SortDescending { get; private set; } = true;

    public void Toggle(DiskTreeRow row)
    {
        if (row.IsExpanded) Collapse(row);
        else Expand(row);
    }

    public void Expand(DiskTreeRow row)
    {
        if (row.IsExpanded || !row.HasChildren) return;

        var insertAt = Rows.IndexOf(row) + 1;
        _expanded.Add(row.Node);
        row.IsExpanded = true;
        foreach (var child in VisibleDescendants(row.Node, row.Depth + 1)) Rows.Insert(insertAt++, child);
    }

    public void Collapse(DiskTreeRow row)
    {
        if (!row.IsExpanded) return;

        var index = Rows.IndexOf(row);
        // Nodes below stay in _expanded, so expanding this row again restores them.
        _expanded.Remove(row.Node);
        row.IsExpanded = false;
        while (index + 1 < Rows.Count && Rows[index + 1].Depth > row.Depth) Rows.RemoveAt(index + 1);
    }

    /// <summary>Sorts every sibling group by the column; choosing the same column again reverses the order.</summary>
    public void SortBy(DiskTreeColumn column)
    {
        if (column == SortColumn)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column != DiskTreeColumn.Name; // names read best A→Z, figures biggest first
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();
        var rootRow = new DiskTreeRow(_root, 0, 100, "") { IsExpanded = _expanded.Contains(_root) };
        Rows.Add(rootRow);
        if (rootRow.IsExpanded)
        {
            foreach (var row in VisibleDescendants(_root, 1)) Rows.Add(row);
        }
    }

    private IEnumerable<DiskTreeRow> VisibleDescendants(DiskNode parent, int depth)
    {
        foreach (var child in Sorted(parent.Children))
        {
            var row = new DiskTreeRow(child, depth, PercentOf(child, parent), ChangeTextFor(child, depth));
            var open = child.Children.Count > 0 && _expanded.Contains(child);
            row.IsExpanded = open;
            yield return row;

            if (open)
            {
                foreach (var descendant in VisibleDescendants(child, depth + 1)) yield return descendant;
            }
        }
    }

    private static double PercentOf(DiskNode child, DiskNode parent)
        => parent.SizeBytes == 0 ? 0 : (double)child.SizeBytes / parent.SizeBytes * 100;

    private string ChangeTextFor(DiskNode node, int depth)
    {
        if (depth != 1 || node.Kind != DiskNodeKind.Folder) return "";
        return _changesByPath.TryGetValue(node.FullPath, out var delta) ? ByteSizeFormatter.FormatSigned(delta) : "";
    }

    private IEnumerable<DiskNode> Sorted(IReadOnlyList<DiskNode> nodes)
    {
        Comparison<DiskNode> byColumn = SortColumn switch
        {
            DiskTreeColumn.Name => (a, b) => NameComparer.Compare(a.Name, b.Name),
            DiskTreeColumn.Files => (a, b) => a.FileCount.CompareTo(b.FileCount),
            DiskTreeColumn.Folders => (a, b) => a.FolderCount.CompareTo(b.FolderCount),
            DiskTreeColumn.Modified => (a, b) => a.LastModifiedUtc.CompareTo(b.LastModifiedUtc),
            _ => (a, b) => a.SizeBytes.CompareTo(b.SizeBytes), // Size and Percent order siblings identically
        };

        var list = nodes.ToList();
        list.Sort((a, b) =>
        {
            var result = byColumn(a, b);
            if (SortDescending) result = -result;
            return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });
        return list;
    }
}
