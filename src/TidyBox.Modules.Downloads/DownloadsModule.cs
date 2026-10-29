using TidyBox.Core.Modules;

namespace TidyBox.Modules.Downloads;

public sealed class DownloadsModule : IToolModule
{
    public DownloadsModule(string? downloadsFolder = null, string? storageDirectory = null)
    {
        DownloadsFolderPath = downloadsFolder ?? DownloadsFolder.Resolve();
        StorageDirectory = storageDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidyBox", "Downloads");
    }

    public string ModuleId => "downloads";

    public string DisplayName => "下载文件夹";

    public string DownloadsFolderPath { get; }

    public string StorageDirectory { get; }

    public Task InitializeAsync()
    {
        // Without a writable storage folder neither the journal nor the kept list can work, so the shell should show the module as failed.
        try
        {
            Directory.CreateDirectory(Path.Combine(StorageDirectory, "journal"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot create the downloads storage folder '{StorageDirectory}'.", ex);
        }

        return Task.CompletedTask;
    }
}
