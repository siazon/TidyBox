using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Tests.Catalog;

public sealed class AppMatcherTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string Dir(string name) => Directory.CreateDirectory(Path.Combine(_base, name)).FullName;

    private static InstalledApp App(string name, string? installLocation)
        => new(name, name, null, null, null, null, AppSource.Win32, installLocation, null, null, null);

    [Fact]
    public void Finds_the_app_whose_folder_contains_the_dropped_file()
    {
        var foo = Dir("Foo");
        var bar = Dir("Bar");
        var apps = new[] { App("Foo", foo), App("Bar", bar) };

        var found = AppMatcher.Find(apps, Path.Combine(foo, "bin", "foo.exe"), Array.Empty<string>());

        Assert.Equal("Foo", found?.DisplayName);
    }

    [Fact]
    public void The_deepest_matching_folder_wins()
    {
        var suite = Dir("Suite");
        var tool = Dir(Path.Combine("Suite", "Tool"));
        var apps = new[] { App("Suite", suite), App("Tool", tool) };

        Assert.Equal("Tool", AppMatcher.Find(apps, Path.Combine(tool, "tool.exe"), Array.Empty<string>())?.DisplayName);
        Assert.Equal("Suite", AppMatcher.Find(apps, Path.Combine(suite, "suite.exe"), Array.Empty<string>())?.DisplayName);
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_does_not_match()
    {
        var foo = Dir("Foo");
        Dir("FooBar");
        var apps = new[] { App("Foo", foo) };

        Assert.Null(AppMatcher.Find(apps, Path.Combine(_base, "FooBar", "x.exe"), Array.Empty<string>()));
    }

    [Fact]
    public void The_folder_itself_and_different_casing_match()
    {
        var foo = Dir("Foo");
        var apps = new[] { App("Foo", foo) };

        Assert.NotNull(AppMatcher.Find(apps, foo, Array.Empty<string>()));
        Assert.NotNull(AppMatcher.Find(apps, Path.Combine(foo.ToUpperInvariant(), "X.EXE"), Array.Empty<string>()));
    }

    [Fact]
    public void Nothing_matches_an_empty_or_relative_path()
    {
        var apps = new[] { App("Foo", Dir("Foo")) };

        Assert.Null(AppMatcher.Find(apps, "", Array.Empty<string>()));
        Assert.Null(AppMatcher.Find(apps, "foo.exe", Array.Empty<string>()));
    }
}
