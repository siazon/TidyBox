using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests.Steam;

public class KnownSharedFoldersTests
{
    [Fact]
    public void Load_ReturnsKnownEntries_NormalizedForLookup()
    {
        var known = KnownSharedFolders.Load();

        Assert.Contains("steamvr", known);
    }

    [Theory]
    [InlineData("SteamVR")]
    [InlineData("steamvr")]
    [InlineData("Steam VR")]
    public void Load_LookupIsCaseAndSpacingInsensitive(string variant)
    {
        var known = KnownSharedFolders.Load();

        Assert.Contains(KnownSharedFolders.Normalize(variant), known);
    }

    [Fact]
    public void Load_UnknownFolderName_IsNotContained()
    {
        var known = KnownSharedFolders.Load();

        Assert.DoesNotContain(KnownSharedFolders.Normalize("SomeRandomGameFolder"), known);
    }
}
