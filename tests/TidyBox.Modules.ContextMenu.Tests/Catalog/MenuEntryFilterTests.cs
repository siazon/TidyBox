using TidyBox.Modules.ContextMenu.Catalog;
using TidyBox.Modules.ContextMenu.Model;

namespace TidyBox.Modules.ContextMenu.Tests.Catalog;

public class MenuEntryFilterTests
{
    private static MenuEntry Entry(
        string displayName = "WinRAR",
        string keyName = "WinRAR32",
        string? publisher = "Alexander Roshal",
        string? modulePath = @"C:\Program Files\WinRAR\rarext.dll",
        string? clsid = "{B41DB860-64E4-11D2-9906-E49FADC173CA}",
        MenuLocation location = MenuLocation.AllFiles)
        => new($"{location}|Handler|{keyName}", location, MenuEntryKind.Handler, keyName, displayName, publisher, modulePath, clsid,
            true, false, MenuEntryState.Enabled, null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_EmptyQuery_MatchesEverything(string? query)
    {
        Assert.True(MenuEntryFilter.Matches(Entry(), query));
    }

    [Theory]
    [InlineData("winrar")]            // display name, any case
    [InlineData("WINRAR32")]          // key name
    [InlineData("roshal")]            // publisher
    [InlineData("rarext")]            // module path
    [InlineData("B41DB860")]          // CLSID fragment
    [InlineData("所有文件")]            // location name
    public void Matches_FindsTheTextInAnyField(string query)
    {
        Assert.True(MenuEntryFilter.Matches(Entry(), query));
    }

    [Fact]
    public void Matches_KeyNameWithLeadingSpace_IsStillFoundByItsName()
    {
        Assert.True(MenuEntryFilter.Matches(Entry(displayName: "FileSyncEx", keyName: " FileSyncEx"), "filesyncex"));
    }

    [Fact]
    public void Matches_SeveralWords_AllMustMatchButInAnyOrderAndAnyField()
    {
        Assert.True(MenuEntryFilter.Matches(Entry(), "roshal winrar"));
        Assert.False(MenuEntryFilter.Matches(Entry(), "winrar nonexistent"));
    }

    [Fact]
    public void Matches_NothingMatches_IsFalse()
    {
        Assert.False(MenuEntryFilter.Matches(Entry(), "zzz-not-here"));
    }

    [Fact]
    public void Matches_EntryWithoutOptionalFields_DoesNotThrow()
    {
        var bare = Entry(publisher: null, modulePath: null, clsid: null);

        Assert.False(MenuEntryFilter.Matches(bare, "roshal"));
        Assert.True(MenuEntryFilter.Matches(bare, "winrar"));
    }
}
