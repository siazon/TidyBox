namespace TidyBox.Modules.Downloads.Tests;

public class DownloadsFolderTests
{
    [Fact]
    public void Resolve_ReturnsFullyQualifiedPath()
    {
        Assert.True(Path.IsPathFullyQualified(DownloadsFolder.Resolve()));
    }
}
