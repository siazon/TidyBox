using PowerTools.Modules.Uninstaller.Icons;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Tests.Icons;

public sealed class AppIconProviderTests : IDisposable
{
    private sealed class FakeExtractor : IIconExtractor
    {
        public List<(IconTarget Target, int Size)> Calls = new();
        public byte[]? Result = new byte[] { 1, 2, 3 };

        public Task<byte[]?> ExtractPngAsync(IconTarget target, int size, CancellationToken ct)
        {
            Calls.Add((target, size));
            return Task.FromResult(Result);
        }
    }

    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly FakeExtractor _extractor = new();

    public AppIconProviderTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private InstalledApp AppWithIcon()
    {
        var exe = Path.Combine(_base, "foo.exe");
        File.WriteAllText(exe, "");
        return new InstalledApp("id", "Foo", null, null, null, null, AppSource.Win32, null, null, null, null) { DisplayIcon = exe };
    }

    [Fact]
    public async Task Returns_the_extracted_png_for_the_resolved_target()
    {
        var provider = new AppIconProvider(_extractor, size: 64);

        var png = await provider.GetPngAsync(AppWithIcon());

        Assert.Equal(new byte[] { 1, 2, 3 }, png);
        Assert.Equal(64, Assert.Single(_extractor.Calls).Size);
    }

    [Fact]
    public async Task Returns_null_without_asking_the_extractor_when_nothing_resolves()
    {
        var provider = new AppIconProvider(_extractor, size: 64);
        var app = new InstalledApp("id", "Nothing", null, null, null, null, AppSource.Win32, null, null, null, null);

        Assert.Null(await provider.GetPngAsync(app));
        Assert.Empty(_extractor.Calls);
    }

    [Fact]
    public async Task The_same_target_is_only_extracted_once()
    {
        var provider = new AppIconProvider(_extractor, size: 64);
        var app = AppWithIcon();

        await provider.GetPngAsync(app);
        await provider.GetPngAsync(app);

        Assert.Single(_extractor.Calls);
    }

    [Fact]
    public async Task A_failed_extraction_gives_null_and_is_not_cached_forever()
    {
        _extractor.Result = null;
        var provider = new AppIconProvider(_extractor, size: 64);
        var app = AppWithIcon();

        Assert.Null(await provider.GetPngAsync(app));

        _extractor.Result = new byte[] { 9 };
        Assert.Equal(new byte[] { 9 }, await provider.GetPngAsync(app));
    }
}
