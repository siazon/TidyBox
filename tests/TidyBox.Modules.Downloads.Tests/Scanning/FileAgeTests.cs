using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tests.Scanning;

public class FileAgeTests
{
    private static readonly TimeSpan Stale = TidyOptions.Default.StaleAfter;

    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static DownloadFile File(DateTime created, DateTime modified) => new(@"C:\d\a.zip", 1, created, modified, null);

    [Fact]
    public void Basis_IsLaterOfCreatedAndModified()
    {
        var created = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);
        var modified = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc); // an extracted file keeps the old time inside the archive

        Assert.Equal(created, FileAge.Basis(File(created, modified)));
        Assert.Equal(created, FileAge.Basis(File(modified, created)));
    }

    [Fact]
    public void IsStale_ExactlyNinetyDays_False() => Assert.False(FileAge.IsStale(File(Now - TimeSpan.FromDays(90), Now - TimeSpan.FromDays(90)), Now, Stale));

    [Fact]
    public void IsStale_NinetyDaysAndOneSecond_True()
    {
        var old = Now - TimeSpan.FromDays(90) - TimeSpan.FromSeconds(1);

        Assert.True(FileAge.IsStale(File(old, old), Now, Stale));
    }

    [Fact]
    public void AgeDays_IsWholeDaysSinceBasis()
    {
        var old = Now - TimeSpan.FromDays(100) - TimeSpan.FromHours(5);

        Assert.Equal(100, FileAge.AgeDays(File(old, old), Now));
    }
}
