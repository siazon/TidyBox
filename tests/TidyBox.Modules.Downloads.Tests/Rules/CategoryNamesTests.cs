using TidyBox.Modules.Downloads.Rules;

namespace TidyBox.Modules.Downloads.Tests.Rules;

public class CategoryNamesTests
{
    [Fact]
    public void FolderFor_DefaultsAreChinese()
    {
        var names = new CategoryNames();

        Assert.Equal(
            new[] { "图片", "文档", "压缩包", "安装包", "音乐", "视频" },
            new[] { "images", "documents", "archives", "installers", "music", "videos" }.Select(names.FolderFor));
    }

    [Fact]
    public void FolderFor_UsesOverride()
    {
        var names = new CategoryNames(new Dictionary<string, string> { ["images"] = "Images" });

        Assert.Equal(("Images", "文档"), (names.FolderFor("images"), names.FolderFor("documents")));
    }

    [Fact]
    public void FolderFor_UnknownId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CategoryNames().FolderFor("nonsense"));
    }

    [Fact]
    public void AllFolderNames_ListsEveryCategoryFolder()
    {
        Assert.Equal(6, new CategoryNames().AllFolderNames.Count);
    }
}
