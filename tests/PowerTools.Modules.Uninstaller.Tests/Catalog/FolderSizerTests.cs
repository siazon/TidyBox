using PowerTools.Modules.Uninstaller.Catalog;

namespace PowerTools.Modules.Uninstaller.Tests.Catalog;

public sealed class FolderSizerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));

    public FolderSizerTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    [Fact]
    public void Adds_up_files_in_all_subfolders()
    {
        Directory.CreateDirectory(Path.Combine(_base, "sub", "deeper"));
        File.WriteAllBytes(Path.Combine(_base, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_base, "sub", "b.bin"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_base, "sub", "deeper", "c.bin"), new byte[300]);

        Assert.Equal(600, FolderSizer.Measure(_base, CancellationToken.None));
    }

    [Fact]
    public void A_missing_folder_has_no_size()
    {
        Assert.Null(FolderSizer.Measure(Path.Combine(_base, "gone"), CancellationToken.None));
    }

    [Fact]
    public void Cancellation_stops_the_walk()
    {
        File.WriteAllBytes(Path.Combine(_base, "a.bin"), new byte[10]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FolderSizer.Measure(_base, cts.Token));
    }
}
