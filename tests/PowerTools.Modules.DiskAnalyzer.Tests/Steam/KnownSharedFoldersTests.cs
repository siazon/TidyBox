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
    [InlineData("Steamworks Shared")]
    [InlineData("steamworks shared")]
    [InlineData("STEAMWORKS SHARED")]
    public void Load_LookupIsCaseInsensitive(string variant)
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

    // Regression: a user's copy of a shared folder (Explorer appends " - 副本" / " - Copy" / "(2)") must NOT be
    // treated as the real shared folder, or a genuine duplicate/orphan copy would be silently hidden.
    [Theory]
    [InlineData("Steamworks Shared - 副本")]
    [InlineData("Steamworks Shared - Copy")]
    [InlineData("Steamworks Shared (2)")]
    public void Load_CopySuffixedName_DoesNotMatchTheOriginal(string copyName)
    {
        var known = KnownSharedFolders.Load();

        Assert.DoesNotContain(KnownSharedFolders.Normalize(copyName), known);
    }
}
