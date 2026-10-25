using PowerTools.Modules.Uninstaller.Icons;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Tests.Icons;

public sealed class IconTargetResolverTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));

    public IconTargetResolverTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string File_(string name)
    {
        var path = Path.Combine(_base, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private static InstalledApp App(string name = "Foo Studio", AppSource source = AppSource.Win32, string? installLocation = null,
        string? displayIcon = null, string? package = null)
        => new("id", name, "Pub", null, null, null, source, installLocation, null, null, package) { DisplayIcon = displayIcon };

    [Fact]
    public void The_registered_display_icon_wins_and_keeps_its_index()
    {
        var exe = File_("foo.exe");

        var target = IconTargetResolver.Resolve(App(displayIcon: $"\"{exe}\",2"));

        Assert.Equal(new IconTarget(exe, 2, null), target);
    }

    [Fact]
    public void A_display_icon_that_points_nowhere_is_skipped()
    {
        var target = IconTargetResolver.Resolve(App(displayIcon: Path.Combine(_base, "gone.exe")));

        Assert.Null(target);
    }

    [Fact]
    public void Without_a_display_icon_an_exe_named_like_the_app_in_its_folder_is_used()
    {
        File_("Foo Studio\\unins000.exe");
        File_("Foo Studio\\updater.exe");
        var main = File_("Foo Studio\\FooStudio.exe");

        var target = IconTargetResolver.Resolve(App(installLocation: Path.GetDirectoryName(main)));

        Assert.Equal(new IconTarget(main, 0, null), target);
    }

    [Fact]
    public void A_folder_without_a_clearly_matching_exe_gives_nothing_rather_than_a_guess()
    {
        File_("Foo Studio\\unins000.exe");
        File_("Foo Studio\\updater.exe");
        File_("Foo Studio\\helper.exe");

        var target = IconTargetResolver.Resolve(App(installLocation: Path.Combine(_base, "Foo Studio")));

        Assert.Null(target);
    }

    [Fact]
    public void Store_apps_are_identified_by_their_package()
    {
        var target = IconTargetResolver.Resolve(App(source: AppSource.Store, package: "Pkg_1.0_x64__abc"));

        Assert.Equal(new IconTarget(null, 0, "Pkg_1.0_x64__abc"), target);
    }

    [Fact]
    public void Steam_games_use_their_display_icon_like_desktop_programs()
    {
        var ico = File_("game.ico");

        var target = IconTargetResolver.Resolve(App(source: AppSource.Steam, displayIcon: ico));

        Assert.Equal(new IconTarget(ico, 0, null), target);
    }

    [Fact]
    public void An_app_with_nothing_to_go_on_has_no_target()
    {
        Assert.Null(IconTargetResolver.Resolve(App()));
    }
}
