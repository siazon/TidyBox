using TidyBox.Core.Recycling;
using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads.Tests;

public class TidyOptionsTests
{
    [Fact]
    public void Default_IsNinetyDaysAndFiveMinutes()
    {
        Assert.Equal((TimeSpan.FromDays(90), TimeSpan.FromMinutes(5)), (TidyOptions.Default.StaleAfter, TidyOptions.Default.RecentWindow));
    }

    [Fact]
    public void Create_KeepsTheValuesGiven()
    {
        var options = TidyOptions.Create(staleDays: 30, recentMinutes: 2);

        Assert.Equal((30, 2, TimeSpan.FromDays(30), TimeSpan.FromMinutes(2)), (options.StaleDays, options.RecentMinutes, options.StaleAfter, options.RecentWindow));
    }

    [Fact]
    public void Create_ZeroMinutes_IsAllowed() => Assert.Equal(TimeSpan.Zero, TidyOptions.Create(90, 0).RecentWindow);

    [Theory]
    [InlineData(0, 5)]
    [InlineData(3651, 5)]
    [InlineData(90, -1)]
    [InlineData(90, 1441)]
    public void Create_OutOfRange_Throws(int days, int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TidyOptions.Create(days, minutes));
    }
}

/// <summary>The two thresholds the user can set: how old a file must be to show up in the review, and how recently written a file is left alone.</summary>
public class ConfigurableOptionsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _base = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());
    private readonly string _root;
    private readonly string _store;
    private readonly Recorder _recycler = new();

    public ConfigurableOptionsTests()
    {
        _root = Path.Combine(_base, "Downloads");
        _store = Path.Combine(_base, "store");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private sealed class Recorder : IFileRecycler
    {
        public List<string> Deleted { get; } = new();

        public void Delete(string filePath) => Deleted.Add(filePath);
    }

    private string Make(string name, TimeSpan age)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "content");
        File.SetCreationTimeUtc(path, Now - age);
        File.SetLastWriteTimeUtc(path, Now - age);
        return path;
    }

    private static DownloadFile Scanned(string path, TimeSpan age) => new(path, 7, Now - age, Now - age, null);

    [Fact]
    public void SafetyFilter_UsesTheGivenWindow()
    {
        var threeMinutes = new FileInfo(Make("a.pdf", TimeSpan.FromMinutes(3)));

        Assert.True(SafetyFilter.IsEligible(threeMinutes, Now, TimeSpan.FromMinutes(1)));
        Assert.False(SafetyFilter.IsEligible(threeMinutes, Now, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void SafetyFilter_ZeroWindow_AcceptsAFileWrittenThisInstant()
    {
        Assert.True(SafetyFilter.IsEligible(new FileInfo(Make("a.pdf", TimeSpan.Zero)), Now, TimeSpan.Zero));
    }

    [Fact]
    public void Scanner_UsesTheGivenWindow()
    {
        Make("a.pdf", TimeSpan.FromMinutes(3));

        Assert.Single(new DownloadScanner().Scan(_root, Array.Empty<string>(), Now, TimeSpan.FromMinutes(1)));
        Assert.Empty(new DownloadScanner().Scan(_root, Array.Empty<string>(), Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void FileAge_UsesTheGivenThreshold()
    {
        var file = Scanned(@"C:\d\a.zip", TimeSpan.FromDays(40));

        Assert.True(FileAge.IsStale(file, Now, TimeSpan.FromDays(30)));
        Assert.False(FileAge.IsStale(file, Now, TimeSpan.FromDays(90)));
    }

    [Fact]
    public void ReviewList_UsesTheGivenThreshold()
    {
        var file = Scanned(@"C:\d\a.zip", TimeSpan.FromDays(40));
        var kept = new KeptStore(Path.Combine(_base, "kept.json"));

        Assert.Single(ReviewList.Build(new[] { file }, kept, Now, TimeSpan.FromDays(30)).Stale);
        Assert.Empty(ReviewList.Build(new[] { file }, kept, Now, TimeSpan.FromDays(90)).Stale);
    }

    [Fact]
    public void Module_Options_StartAsGiven()
    {
        var module = new DownloadsModule(_root, _store, options: TidyOptions.Create(30, 2));

        Assert.Equal((30, 2), (module.Options.StaleDays, module.Options.RecentMinutes));
    }

    [Fact]
    public async Task Module_SetOptions_ChangesWhatCountsAsStale_OnTheNextScan()
    {
        Make("mid.exe", TimeSpan.FromDays(40));
        var module = new DownloadsModule(_root, _store, _recycler, () => Now);
        Assert.Empty((await module.ScanAsync()).Stale);

        module.SetOptions(TidyOptions.Create(30, 5));

        Assert.Equal("mid.exe", Assert.Single((await module.ScanAsync()).Stale).Name);
    }

    [Fact]
    public async Task Module_SetOptions_ChangesTheWindowThatProtectsFilesFromRecycling()
    {
        var path = Make("recent.exe", TimeSpan.FromMinutes(3));
        var file = Scanned(path, TimeSpan.FromMinutes(3)) with { SizeBytes = new FileInfo(path).Length };
        var module = new DownloadsModule(_root, _store, _recycler, () => Now);

        var refused = await module.RecycleAsync(new[] { file });
        Assert.Single(refused.Failures);
        Assert.Empty(_recycler.Deleted);

        module.SetOptions(TidyOptions.Create(90, 1));
        var allowed = await module.RecycleAsync(new[] { file });

        Assert.Empty(allowed.Failures);
        Assert.Equal(new[] { path }, _recycler.Deleted);
    }
}
