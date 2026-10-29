using System.Collections.Specialized;
using System.ComponentModel;
using TidyBox.Modules.DiskAnalyzer.History;
using TidyBox.Modules.DiskAnalyzer.Scanning;
using TidyBox.Modules.DiskAnalyzer.Tree;

namespace TidyBox.Modules.DiskAnalyzer.Tests.Tree;

public class DiskTreeModelTests
{
    private const string RootPath = @"C:\Root";

    private static DiskNode Folder(string name, long size, int files = 1, int folders = 0, DateTime? modified = null, params DiskNode[] children)
        => new(name, Path.Combine(RootPath, name), DiskNodeKind.Folder, size, files, folders,
            modified ?? new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), children);

    private static DiskNode Group(long size, int files)
        => new($"[{files} 个文件]", RootPath, DiskNodeKind.FilesGroup, size, files, 0, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), Array.Empty<DiskNode>());

    private static DiskNode Root(params DiskNode[] children)
        => new(RootPath, RootPath, DiskNodeKind.Folder, children.Sum(c => c.SizeBytes), children.Sum(c => c.FileCount),
            children.Sum(c => c.FolderCount) + children.Count(c => c.Kind == DiskNodeKind.Folder),
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), children.OrderByDescending(c => c.SizeBytes).ToList());

    private static string[] Names(DiskTreeModel model) => model.Rows.Select(r => r.Name).ToArray();

    // Root
    //   Big (600)  -> Inner (400) -> Leaf (300)
    //                 [3 files] (200)
    //   Mid (300)
    //   Small (100)
    private static DiskTreeModel SampleModel(IReadOnlyList<DiskScanEntryDelta>? changes = null)
    {
        var leaf = Folder("Leaf", 300);
        var inner = Folder("Inner", 400, files: 2, folders: 1, children: leaf);
        var big = Folder("Big", 600, files: 3, folders: 2, children: new[] { inner, Group(200, 3) });
        return new DiskTreeModel(Root(big, Folder("Mid", 300), Folder("Small", 100)), changes);
    }

    [Fact]
    public void Initially_ShowsTheExpandedRootAndItsChildrenLargestFirst()
    {
        var model = SampleModel();

        Assert.Equal(new[] { RootPath, "Big", "Mid", "Small" }, Names(model));
        Assert.Equal(new[] { 0, 1, 1, 1 }, model.Rows.Select(r => r.Depth));
        Assert.True(model.Rows[0].IsExpanded);
        Assert.All(model.Rows.Skip(1), r => Assert.False(r.IsExpanded));
    }

    [Fact]
    public void PercentOfParent_IsRelativeToTheParentFolder()
    {
        var model = SampleModel();

        Assert.Equal(100, model.Rows[0].PercentOfParent);
        Assert.Equal(60, model.Rows[1].PercentOfParent, precision: 5);  // 600 of 1000
        Assert.Equal(10, model.Rows[3].PercentOfParent, precision: 5);  // 100 of 1000

        model.Expand(model.Rows[1]);
        Assert.Equal(400.0 / 600 * 100, model.Rows[2].PercentOfParent, precision: 5);
    }

    [Fact]
    public void PercentOfParent_ParentWithZeroSize_IsZeroNotNaN()
    {
        var model = new DiskTreeModel(Root(Folder("Empty", 0, files: 0)));

        Assert.Equal(0, model.Rows[1].PercentOfParent);
    }

    [Fact]
    public void Expand_InsertsChildrenRightAfterTheRowOneLevelDeeper()
    {
        var model = SampleModel();

        model.Expand(model.Rows[1]); // Big

        Assert.Equal(new[] { RootPath, "Big", "Inner", "[3 个文件]", "Mid", "Small" }, Names(model));
        Assert.Equal(new[] { 0, 1, 2, 2, 1, 1 }, model.Rows.Select(r => r.Depth));
        Assert.True(model.Rows[1].IsExpanded);
    }

    [Fact]
    public void Collapse_RemovesAllDescendantRows()
    {
        var model = SampleModel();
        model.Expand(model.Rows[1]);
        model.Expand(model.Rows[2]); // Inner -> Leaf

        model.Collapse(model.Rows[1]);

        Assert.Equal(new[] { RootPath, "Big", "Mid", "Small" }, Names(model));
        Assert.False(model.Rows[1].IsExpanded);
    }

    [Fact]
    public void ReExpanding_RestoresWhatWasExpandedBelow()
    {
        var model = SampleModel();
        model.Expand(model.Rows[1]);
        model.Expand(model.Rows[2]); // Inner
        model.Collapse(model.Rows[1]);

        model.Expand(model.Rows[1]);

        Assert.Equal(new[] { RootPath, "Big", "Inner", "Leaf", "[3 个文件]", "Mid", "Small" }, Names(model));
        Assert.True(model.Rows[2].IsExpanded);
    }

    [Fact]
    public void Toggle_FlipsBetweenExpandedAndCollapsed()
    {
        var model = SampleModel();

        model.Toggle(model.Rows[1]);
        Assert.Equal(6, model.Rows.Count);
        model.Toggle(model.Rows[1]);
        Assert.Equal(4, model.Rows.Count);
    }

    [Fact]
    public void Expand_OnALeaf_DoesNothing()
    {
        var model = SampleModel();

        model.Expand(model.Rows[2]); // Mid has no children

        Assert.False(model.Rows[2].IsExpanded);
        Assert.Equal(4, model.Rows.Count);
        Assert.False(model.Rows[2].HasChildren);
    }

    [Fact]
    public void ExpandAndCollapse_AreIncrementalSoTheListKeepsItsScrollPosition()
    {
        var model = SampleModel();
        var actions = new List<NotifyCollectionChangedAction>();
        model.Rows.CollectionChanged += (_, e) => actions.Add(e.Action);

        model.Expand(model.Rows[1]);
        model.Collapse(model.Rows[1]);

        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Contains(NotifyCollectionChangedAction.Add, actions);
        Assert.Contains(NotifyCollectionChangedAction.Remove, actions);
    }

    [Fact]
    public void IsExpanded_RaisesPropertyChanged()
    {
        var model = SampleModel();
        var row = model.Rows[1];
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Expand(row);
        model.Collapse(row);

        Assert.Equal(new[] { nameof(DiskTreeRow.IsExpanded), nameof(DiskTreeRow.IsExpanded) }, changed);
    }

    [Fact]
    public void SortBy_Name_SortsEverySiblingGroupAndSecondCallReverses()
    {
        var model = SampleModel();
        model.Expand(model.Rows[1]); // Big -> Inner, [3 个文件]

        model.SortBy(DiskTreeColumn.Name);
        Assert.Equal(new[] { RootPath, "Big", "[3 个文件]", "Inner", "Mid", "Small" }, Names(model));

        model.SortBy(DiskTreeColumn.Name);
        Assert.Equal(new[] { RootPath, "Small", "Mid", "Big", "Inner", "[3 个文件]" }, Names(model));
    }

    [Fact]
    public void SortBy_Size_IsDescendingFirstThenAscending()
    {
        var model = SampleModel();
        model.SortBy(DiskTreeColumn.Name);

        model.SortBy(DiskTreeColumn.Size);
        Assert.Equal(new[] { "Big", "Mid", "Small" }, Names(model).Skip(1));

        model.SortBy(DiskTreeColumn.Size);
        Assert.Equal(new[] { "Small", "Mid", "Big" }, Names(model).Skip(1));
        Assert.Equal(DiskTreeColumn.Size, model.SortColumn);
        Assert.False(model.SortDescending);
    }

    [Fact]
    public void SortBy_FilesFoldersAndModified_UseThoseFigures()
    {
        var model = new DiskTreeModel(Root(
            Folder("A", 10, files: 5, folders: 1, modified: new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
            Folder("B", 20, files: 9, folders: 7, modified: new DateTime(2022, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
            Folder("C", 30, files: 1, folders: 3, modified: new DateTime(2021, 1, 1, 12, 0, 0, DateTimeKind.Utc))));

        model.SortBy(DiskTreeColumn.Files);
        Assert.Equal(new[] { "B", "A", "C" }, Names(model).Skip(1));
        model.SortBy(DiskTreeColumn.Folders);
        Assert.Equal(new[] { "B", "C", "A" }, Names(model).Skip(1));
        model.SortBy(DiskTreeColumn.Modified);
        Assert.Equal(new[] { "B", "C", "A" }, Names(model).Skip(1));
    }

    [Fact]
    public void SortBy_KeepsWhatIsExpanded()
    {
        var model = SampleModel();
        model.Expand(model.Rows[1]);

        model.SortBy(DiskTreeColumn.Name);

        Assert.True(model.Rows.Single(r => r.Name == "Big").IsExpanded);
        Assert.Contains("Inner", Names(model));
    }

    [Fact]
    public void Columns_AreFormattedForDisplay()
    {
        var root = Root(new DiskNode("Games", RootPath + @"\Games", DiskNodeKind.Folder, 5368709120, 803747, 88844,
            new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc), Array.Empty<DiskNode>()));
        var row = new DiskTreeModel(root).Rows[1];

        Assert.Equal("5.0 GB", row.SizeText);
        Assert.Equal("803,747", row.FilesText);
        Assert.Equal("88,844", row.FoldersText);
        Assert.Equal("100.0 %", row.PercentText);
        Assert.Matches(@"^\d{4}/\d{1,2}/\d{1,2}$", row.ModifiedText);
    }

    [Fact]
    public void ToString_IsReadableForScreenReaders()
    {
        Assert.Equal("Big, 600 B, 60.0 %", SampleModel().Rows[1].ToString());
    }

    [Fact]
    public void ChangeText_IsFilledForTopLevelFoldersOnly()
    {
        var changes = new[]
        {
            new DiskScanEntryDelta(Path.Combine(RootPath, "Big"), 2048, 100, 2148),
            new DiskScanEntryDelta(Path.Combine(RootPath, "Mid"), -1024, 1324, 300),
            new DiskScanEntryDelta(Path.Combine(RootPath, "Small"), 0, 100, 100),
        };
        var model = SampleModel(changes);
        model.Expand(model.Rows[1]);

        Assert.Equal("", model.Rows[0].ChangeText);                            // root
        Assert.Equal("+2.0 KB", model.Rows.Single(r => r.Name == "Big").ChangeText);
        Assert.Equal("-1.0 KB", model.Rows.Single(r => r.Name == "Mid").ChangeText);
        Assert.Equal("", model.Rows.Single(r => r.Name == "Small").ChangeText);                // unchanged: stay quiet
        Assert.Equal("", model.Rows.Single(r => r.Name == "Inner").ChangeText); // deeper rows have no history
        Assert.Equal("", model.Rows.Single(r => r.Name == "[3 个文件]").ChangeText);
    }

    [Fact]
    public void ChangeText_WithoutPreviousScan_IsEmpty()
    {
        Assert.All(SampleModel(changes: null).Rows, r => Assert.Equal("", r.ChangeText));
    }

    [Fact]
    public void ChangeText_ForAFolderMissingFromTheComparison_IsEmpty()
    {
        var model = SampleModel(new[] { new DiskScanEntryDelta(Path.Combine(RootPath, "Big"), 5, 0, 5) });

        Assert.Equal("", model.Rows.Single(r => r.Name == "Mid").ChangeText);
    }
}
