using TidyBox.Modules.Downloads.Source;

namespace TidyBox.Modules.Downloads.Tests.Source;

public class SourceReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public SourceReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void TryGetDomain_ReadsRealAlternateStream()
    {
        var path = Path.Combine(_dir, "a.pdf");
        File.WriteAllText(path, "x");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/a\r\n");

        Assert.Equal("example.com", SourceReader.TryGetDomain(path));
    }

    [Fact]
    public void TryGetDomain_FileWithoutStream_ReturnsNull()
    {
        var path = Path.Combine(_dir, "plain.pdf");
        File.WriteAllText(path, "x");

        Assert.Null(SourceReader.TryGetDomain(path));
    }

    [Fact]
    public void TryGetDomain_MissingFile_ReturnsNull() => Assert.Null(SourceReader.TryGetDomain(Path.Combine(_dir, "nope.pdf")));
}
