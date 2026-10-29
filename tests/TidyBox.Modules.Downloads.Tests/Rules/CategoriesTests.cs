using TidyBox.Modules.Downloads.Rules;

namespace TidyBox.Modules.Downloads.Tests.Rules;

public class CategoriesTests
{
    [Theory]
    [InlineData("a.JPG", "images")]
    [InlineData("r.pdf", "documents")]
    [InlineData("x.7z", "archives")]
    [InlineData("s.msi", "installers")]
    [InlineData("s.flac", "music")]
    [InlineData("v.webm", "videos")]
    public void ForFile_MapsExtensionCaseInsensitively(string fileName, string expectedId)
    {
        Assert.Equal(expectedId, Categories.ForFile(fileName)?.Id);
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("a.xpi")]
    [InlineData("b.apk")]
    [InlineData("noextension")]
    public void ForFile_UnknownExtension_ReturnsNull(string fileName)
    {
        Assert.Null(Categories.ForFile(fileName));
    }

    [Fact]
    public void EveryExtension_BelongsToExactlyOneCategory()
    {
        var all = Categories.All.SelectMany(category => category.Extensions).ToList();

        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_HasTheSixDefaultCategories()
    {
        Assert.Equal(new[] { "archives", "documents", "images", "installers", "music", "videos" }, Categories.All.Select(c => c.Id).OrderBy(id => id));
    }
}
