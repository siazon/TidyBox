using System.Text.Json;
using PowerTools.ElevatedHelper;

namespace PowerTools.ElevatedHelper.Tests;

public sealed class QuarantineInspectHandlerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private string Quarantine => Path.Combine(_base, "Quarantine");

    public void Dispose()
    {
        if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true);
    }

    private QuarantineInspectHandler Handler() => new(Quarantine);

    private (long TotalBytes, int BatchCount) RunAndParse()
    {
        var response = Handler().HandleAsync("{}").GetAwaiter().GetResult();
        var doc = JsonDocument.Parse(response.ResultJson!);
        return (doc.RootElement.GetProperty("TotalBytes").GetInt64(), doc.RootElement.GetProperty("BatchCount").GetInt32());
    }

    [Fact]
    public void HandleAsync_NoQuarantineFolder_ReturnsZero()
    {
        var (total, batches) = RunAndParse();

        Assert.Equal(0, total);
        Assert.Equal(0, batches);
    }

    [Fact]
    public void HandleAsync_SingleBatchWithFiles_ReturnsItsSize()
    {
        var batch = Directory.CreateDirectory(Path.Combine(Quarantine, "20260101-000000")).FullName;
        File.WriteAllBytes(Path.Combine(batch, "a.bin"), new byte[100]);

        var (total, batches) = RunAndParse();

        Assert.Equal(100, total);
        Assert.Equal(1, batches);
    }

    [Fact]
    public void HandleAsync_MultipleBatches_SumsAllOfThem()
    {
        var batch1 = Directory.CreateDirectory(Path.Combine(Quarantine, "20260101-000000")).FullName;
        File.WriteAllBytes(Path.Combine(batch1, "a.bin"), new byte[100]);
        var batch2 = Directory.CreateDirectory(Path.Combine(Quarantine, "20260102-000000")).FullName;
        File.WriteAllBytes(Path.Combine(batch2, "b.bin"), new byte[50]);

        var (total, batches) = RunAndParse();

        Assert.Equal(150, total);
        Assert.Equal(2, batches);
    }
}

public sealed class QuarantinePurgeHandlerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private string Quarantine => Path.Combine(_base, "Quarantine");

    public void Dispose()
    {
        if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true);
    }

    private QuarantinePurgeHandler Handler() => new(Quarantine, trust: TestTrust.CurrentUser);

    [Fact]
    public async Task HandleAsync_NoQuarantineFolder_SucceedsWithoutError()
    {
        var response = await Handler().HandleAsync("{}");

        Assert.True(response.Success);
    }

    [Fact]
    public async Task HandleAsync_DeletesAllBatches()
    {
        var batch1 = Directory.CreateDirectory(Path.Combine(Quarantine, "20260101-000000")).FullName;
        File.WriteAllBytes(Path.Combine(batch1, "a.bin"), new byte[1]);
        Directory.CreateDirectory(Path.Combine(Quarantine, "20260102-000000"));

        var response = await Handler().HandleAsync("{}");

        Assert.True(response.Success);
        Assert.True(Directory.Exists(Quarantine)); // the root itself stays (and stays secured); only the batches are gone
        Assert.Empty(Directory.EnumerateDirectories(Quarantine));
    }
}
