using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tests.Scanning;

public class SafetyFilterTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public SafetyFilterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private FileInfo Make(string name, int bytes = 10, TimeSpan? age = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, Now - (age ?? TimeSpan.FromHours(1)));
        return new FileInfo(path);
    }

    [Fact]
    public void OrdinaryFile_IsEligible() => Assert.True(SafetyFilter.IsEligible(Make("report.pdf"), Now));

    [Theory]
    [InlineData("a.crdownload")]
    [InlineData("a.part")]
    [InlineData("a.tmp")]
    [InlineData("a.partial")]
    [InlineData("a.opdownload")]
    [InlineData("desktop.ini")]
    [InlineData("Thumbs.db")]
    public void UnfinishedOrSystemFile_IsNotEligible(string name) => Assert.False(SafetyFilter.IsEligible(Make(name), Now));

    [Fact]
    public void ZeroByteFile_IsNotEligible() => Assert.False(SafetyFilter.IsEligible(Make("empty.pdf", bytes: 0), Now));

    [Fact]
    public void WrittenFourMinutesAgo_IsNotEligible() => Assert.False(SafetyFilter.IsEligible(Make("a.pdf", age: TimeSpan.FromMinutes(4)), Now));

    [Fact]
    public void WrittenSixMinutesAgo_IsEligible() => Assert.True(SafetyFilter.IsEligible(Make("a.pdf", age: TimeSpan.FromMinutes(6)), Now));

    [Fact]
    public void FileOpenedWithShareNone_IsNotEligible()
    {
        var file = Make("locked.pdf");
        using var held = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.False(SafetyFilter.IsEligible(file, Now));
    }

    [Fact]
    public void SymbolicLinkFile_IsNotEligible()
    {
        var target = Make("real.pdf");
        var link = Path.Combine(_dir, "link.pdf");

        // Symbolic links need developer mode or elevation; on a machine without either there is nothing to test.
        if (!TestLinks.TryCreateFileLink(link, target.FullName)) return;

        Assert.False(SafetyFilter.IsEligible(new FileInfo(link), Now));
    }
}
