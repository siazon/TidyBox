using TidyBox.Modules.Downloads.Source;

namespace TidyBox.Modules.Downloads.Tests.Source;

public class ZoneIdentifierParserTests
{
    private static string Zone(params string[] lines) => "[ZoneTransfer]\r\nZoneId=3\r\n" + string.Join("\r\n", lines) + "\r\n";

    [Fact]
    public void ReferrerUrl_IsPreferredOverHostUrl()
    {
        var content = Zone("ReferrerUrl=https://github.com/owner/repo", "HostUrl=https://release-assets.githubusercontent.com/x.zip");

        Assert.Equal("github.com", ZoneIdentifierParser.ParseDomain(content));
    }

    [Fact]
    public void OnlyHostUrl_FallsBackToIt()
    {
        Assert.Equal("www.voidtools.com", ZoneIdentifierParser.ParseDomain(Zone("HostUrl=https://www.voidtools.com/Everything.zip")));
    }

    [Fact]
    public void AboutInternet_IsIgnored_ReturnsNull()
    {
        Assert.Null(ZoneIdentifierParser.ParseDomain(Zone("HostUrl=about:internet")));
    }

    [Fact]
    public void ReferrerAboutInternet_FallsBackToHostUrl()
    {
        var content = Zone("ReferrerUrl=about:internet", "HostUrl=https://example.com/f.pdf");

        Assert.Equal("example.com", ZoneIdentifierParser.ParseDomain(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("[ZoneTransfer]\r\nZoneId=3")]
    [InlineData("HostUrl=not a url")]
    [InlineData("HostUrl=ftp://x.com/f")]
    [InlineData("HostUrl=")]
    public void MissingOrUnusableInput_ReturnsNull(string content)
    {
        Assert.Null(ZoneIdentifierParser.ParseDomain(content));
    }

    [Fact]
    public void UrlWithQueryAndToken_ReturnsOnlyTheHost()
    {
        var content = Zone("ReferrerUrl=https://Example.COM:8443/private/path?token=SECRET#frag");

        Assert.Equal("example.com", ZoneIdentifierParser.ParseDomain(content));
    }
}
