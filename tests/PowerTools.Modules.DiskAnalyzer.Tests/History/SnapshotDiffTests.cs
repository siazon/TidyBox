using PowerTools.Modules.DiskAnalyzer.History;
using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer.Tests.History;

public class SnapshotDiffTests
{
    private static DiskScanSnapshot Snapshot(params (string Path, long Size)[] entries)
        => new(@"C:\Root", DateTime.UtcNow,
            entries.Select(e => new DiskScanEntry(e.Path, e.Size, DateTime.UtcNow, 1)).ToList());

    [Fact]
    public void Compare_FolderGrew_ReportsPositiveDelta()
    {
        var deltas = SnapshotDiff.Compare(Snapshot(("A", 100)), Snapshot(("A", 300)));

        var a = deltas.Single(d => d.Path == "A");
        Assert.Equal(200, a.SizeDeltaBytes);
        Assert.Equal(100, a.PreviousSizeBytes);
        Assert.Equal(300, a.CurrentSizeBytes);
    }

    [Fact]
    public void Compare_FolderDisappeared_ReportsFullNegativeDeltaWithoutThrowing()
    {
        var deltas = SnapshotDiff.Compare(Snapshot(("A", 100), ("B", 50)), Snapshot(("A", 100)));

        var b = deltas.Single(d => d.Path == "B");
        Assert.Equal(0, b.CurrentSizeBytes);
        Assert.Equal(-50, b.SizeDeltaBytes);
    }

    [Fact]
    public void Compare_NewFolder_ReportsItsFullSizeAsGrowth()
    {
        var deltas = SnapshotDiff.Compare(Snapshot(("A", 100)), Snapshot(("A", 100), ("New", 70)));

        var added = deltas.Single(d => d.Path == "New");
        Assert.Equal(0, added.PreviousSizeBytes);
        Assert.Equal(70, added.SizeDeltaBytes);
    }

    [Fact]
    public void Compare_IsOrderedByAbsoluteDeltaDescending()
    {
        var previous = Snapshot(("Small", 10), ("Shrunk", 1000), ("Grew", 100), ("Same", 5));
        var current = Snapshot(("Small", 20), ("Shrunk", 400), ("Grew", 350), ("Same", 5));

        var deltas = SnapshotDiff.Compare(previous, current);

        Assert.Equal(new[] { "Shrunk", "Grew", "Small", "Same" }, deltas.Select(d => d.Path));
    }

    [Fact]
    public void Compare_PathsDifferingOnlyInCase_AreTheSameFolder()
    {
        var deltas = SnapshotDiff.Compare(Snapshot((@"C:\Root\Games", 100)), Snapshot((@"c:\root\GAMES", 150)));

        Assert.Equal(50, deltas.Single().SizeDeltaBytes);
    }
}
