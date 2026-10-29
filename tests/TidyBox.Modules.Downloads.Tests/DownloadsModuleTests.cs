namespace TidyBox.Modules.Downloads.Tests;

public class DownloadsModuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public DownloadsModuleTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ModuleId_And_DisplayName()
    {
        var module = new DownloadsModule(downloadsFolder: _root, storageDirectory: Path.Combine(_root, "store"));

        Assert.Equal(("downloads", "下载文件夹"), (module.ModuleId, module.DisplayName));
    }

    [Fact]
    public async Task InitializeAsync_CreatesStorageDirectory_WithJournalSubfolder()
    {
        var store = Path.Combine(_root, "store");
        var module = new DownloadsModule(downloadsFolder: _root, storageDirectory: store);

        await module.InitializeAsync();

        Assert.True(Directory.Exists(store));
        Assert.True(Directory.Exists(Path.Combine(store, "journal")));
    }

    [Fact]
    public async Task InitializeAsync_StorageDirectoryIsAFile_ThrowsInvalidOperationException()
    {
        var store = Path.Combine(_root, "store");
        File.WriteAllText(store, "not a folder");
        var module = new DownloadsModule(downloadsFolder: _root, storageDirectory: store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => module.InitializeAsync());
    }

    [Fact]
    public void Module_UsesOverrideFolder()
    {
        var module = new DownloadsModule(downloadsFolder: _root);

        Assert.Equal(_root, module.DownloadsFolderPath);
    }
}
