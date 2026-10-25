using PowerTools.Modules.Uninstaller.Icons;
using Windows.Management.Deployment;

namespace PowerTools.Modules.Uninstaller.Tests.Icons;

/// <summary>These talk to the real shell: they prove the interop works, not what a particular icon looks like.</summary>
public class ShellIconExtractorTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static void AssertPng(byte[]? png)
    {
        Assert.NotNull(png);
        Assert.True(png!.Length > 200);
        Assert.Equal(PngSignature, png.Take(8).ToArray());
    }

    private readonly ShellIconExtractor _extractor = new();

    [Fact]
    public async Task Extracts_the_icon_of_an_exe_as_png()
    {
        var png = await _extractor.ExtractPngAsync(new IconTarget(Path.Combine(Environment.SystemDirectory, "notepad.exe"), 0, null), 64, CancellationToken.None);

        AssertPng(png);
    }

    [Fact]
    public async Task Extracts_an_icon_by_index_from_a_resource_file()
    {
        var png = await _extractor.ExtractPngAsync(new IconTarget(Path.Combine(Environment.SystemDirectory, "imageres.dll"), 5, null), 64, CancellationToken.None);

        AssertPng(png);
    }

    [Fact]
    public async Task A_file_that_does_not_exist_gives_null()
    {
        var png = await _extractor.ExtractPngAsync(new IconTarget(@"C:\definitely\not\here.exe", 0, null), 64, CancellationToken.None);

        Assert.Null(png);
    }

    [Fact]
    public async Task A_package_that_is_not_installed_gives_null()
    {
        var png = await _extractor.ExtractPngAsync(new IconTarget(null, 0, "No.Such.Package_1.0.0.0_x64__zzzzzzzzzzzzz"), 64, CancellationToken.None);

        Assert.Null(png);
    }

    [Fact]
    public async Task Extracts_the_logo_of_an_installed_store_app()
    {
        var package = new PackageManager().FindPackagesForUser("").FirstOrDefault(p => p.Id.Name == "Microsoft.WindowsCalculator");
        if (package is null) return; // not every machine has it

        var png = await _extractor.ExtractPngAsync(new IconTarget(null, 0, package.Id.FullName), 64, CancellationToken.None);

        AssertPng(png);
    }
}
