using TidyBox.Modules.Uninstaller.Icons;
using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Tests.Icons;

public sealed class IconTargetResolverTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "TidyBoxTests", Guid.NewGuid().ToString("N"));

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
    public void An_exe_in_bin_named_after_one_word_of_the_app_name_is_used()
    {
        File_("Android Studio\\bin\\elevator.exe");
        File_("Android Studio\\bin\\launcher.exe");
        File_("Android Studio\\bin\\WinProcessListHelper.exe");
        var main = File_("Android Studio\\bin\\studio64.exe");

        var target = IconTargetResolver.Resolve(App("Android Studio", installLocation: Path.Combine(_base, "Android Studio")));

        Assert.Equal(new IconTarget(main, 0, null), target);
    }

    [Fact]
    public void The_64_bit_exe_is_preferred_over_its_32_bit_twin()
    {
        File_("Foo Studio\\studio.exe");
        var x64 = File_("Foo Studio\\studio64.exe");

        var target = IconTargetResolver.Resolve(App("Android Studio", installLocation: Path.Combine(_base, "Foo Studio")));

        Assert.Equal(x64, target?.File);
    }

    [Fact]
    public void Two_different_words_matching_two_exes_is_too_ambiguous_to_pick()
    {
        File_("Foo Bar\\android.exe");
        File_("Foo Bar\\studio.exe");

        Assert.Null(IconTargetResolver.Resolve(App("Android Studio", installLocation: Path.Combine(_base, "Foo Bar"))));
    }

    [Fact]
    public void Updaters_and_installers_are_never_picked_even_when_the_name_matches()
    {
        File_("Foo\\update.exe");
        File_("Foo\\uninstall.exe");

        Assert.Null(IconTargetResolver.Resolve(App("Foo Update Uninstall", installLocation: Path.Combine(_base, "Foo"))));
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
