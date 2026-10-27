using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests.Steam;

public class SteamManifestWriterTests : IDisposable
{
    private readonly string _lib = Path.Combine(Path.GetTempPath(), "pt-manifest-" + Guid.NewGuid());

    public SteamManifestWriterTests() => Directory.CreateDirectory(Path.Combine(_lib, "steamapps"));

    public void Dispose() => Directory.Delete(_lib, recursive: true);

    [Fact]
    public void Write_CreatesManifestThatTheLibraryParserReadsBack()
    {
        var path = SteamManifestWriter.Write(_lib, "413150", "Stardew Valley", "StardewValley", 1234);

        Assert.Equal(Path.Combine(_lib, "steamapps", "appmanifest_413150.acf"), path);
        var game = Assert.Single(new SteamLibraryParser().ParseLibrary(_lib));
        Assert.Equal("413150", game.AppId);
        Assert.Equal("Stardew Valley", game.Name);
        Assert.Equal(1234, game.SizeBytes);
        Assert.Equal(Path.Combine(_lib, "steamapps", "common", "StardewValley"), game.InstallPath);
    }

    [Fact]
    public void Write_NameWithQuotesAndBackslashes_IsEscapedAndRoundTrips()
    {
        SteamManifestWriter.Write(_lib, "1", "The \"Odd\" Game \\ Deluxe", "Odd", 0);

        Assert.Equal("The \"Odd\" Game \\ Deluxe", Assert.Single(new SteamLibraryParser().ParseLibrary(_lib)).Name);
    }

    [Fact]
    public void Write_ManifestAlreadyExists_ThrowsAndLeavesItUntouched()
    {
        var existing = Path.Combine(_lib, "steamapps", "appmanifest_5.acf");
        File.WriteAllText(existing, "original");

        Assert.Throws<IOException>(() => SteamManifestWriter.Write(_lib, "5", "G", "G", 0));
        Assert.Equal("original", File.ReadAllText(existing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("12/../x")]
    [InlineData("99999999999")]
    public void NormalizeAppId_Invalid_Throws(string appId)
    {
        Assert.Throws<ArgumentException>(() => SteamManifestWriter.NormalizeAppId(appId));
    }

    [Fact]
    public void NormalizeAppId_TrimsWhitespaceAndLeadingZeros()
    {
        Assert.Equal("730", SteamManifestWriter.NormalizeAppId(" 0730 "));
    }
}
