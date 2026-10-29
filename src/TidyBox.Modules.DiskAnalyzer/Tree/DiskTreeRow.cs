using System.ComponentModel;
using System.Globalization;
using TidyBox.Modules.DiskAnalyzer.Scanning;

namespace TidyBox.Modules.DiskAnalyzer.Tree;

/// <summary>One visible line of the tree table. Text columns are preformatted so the view only binds.</summary>
public sealed class DiskTreeRow : INotifyPropertyChanged
{
    private bool _isExpanded;

    internal DiskTreeRow(DiskNode node, int depth, double percentOfParent, string changeText)
    {
        Node = node;
        Depth = depth;
        PercentOfParent = percentOfParent;
        ChangeText = changeText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DiskNode Node { get; }

    public int Depth { get; }

    /// <summary>0–100, this node's share of its parent folder's size.</summary>
    public double PercentOfParent { get; }

    /// <summary>Size change since the previous scan; only top-level folders that changed have one, otherwise empty.</summary>
    public string ChangeText { get; }

    public bool HasChildren => Node.Children.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        internal set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public DiskNodeKind Kind => Node.Kind;

    public string Name => Node.Name;

    public string SizeText => ByteSizeFormatter.Format(Node.SizeBytes);

    public string FilesText => Node.FileCount.ToString("N0", CultureInfo.InvariantCulture);

    public string FoldersText => Node.FolderCount.ToString("N0", CultureInfo.InvariantCulture);

    public string PercentText => PercentOfParent.ToString("0.0", CultureInfo.InvariantCulture) + " %";

    public string ModifiedText => Node.LastModifiedUtc.ToLocalTime().ToString("yyyy/M/d", CultureInfo.InvariantCulture);

    // What screen readers announce for the list item.
    public override string ToString() => $"{Name}, {SizeText}, {PercentText}";
}
