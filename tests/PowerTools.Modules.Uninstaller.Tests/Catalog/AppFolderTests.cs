using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Tests.Catalog;

public sealed class AppFolderTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly string _programFiles;

    public AppFolderTests() => _programFiles = Directory.CreateDirectory(Path.Combine(_base, "Program Files")).FullName;

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string[] Roots => new[] { _programFiles };

    private static InstalledApp App(string? installLocation = null, string? uninstall = null)
        => new("id", "App", null, null, null, null, AppSource.Win32, installLocation, uninstall, null, null);

    [Fact]
    public void Uses_the_registered_install_location_when_it_exists()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_base, "Apps", "Foo")).FullName;

        Assert.Equal(dir, AppFolder.Resolve(App(installLocation: dir + "\\"), Roots));
    }

    [Fact]
    public void Falls_back_to_the_folder_of_the_uninstaller_when_it_lives_under_program_files()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_programFiles, "Vendor", "Foo")).FullName;

        var folder = AppFolder.Resolve(App(uninstall: $"\"{dir}\\uninstall.exe\" /S"), Roots);

        Assert.Equal(dir, folder);
    }

    [Fact]
    public void A_missing_install_location_still_falls_back_to_the_uninstaller_folder()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_programFiles, "Foo")).FullName;

        var folder = AppFolder.Resolve(App(installLocation: Path.Combine(_base, "gone"), uninstall: $"\"{dir}\\u.exe\""), Roots);

        Assert.Equal(dir, folder);
    }

    [Fact]
    public void Returns_null_when_the_uninstaller_is_directly_in_the_root_or_outside_it()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(_base, "Elsewhere")).FullName;

        Assert.Null(AppFolder.Resolve(App(uninstall: $"\"{_programFiles}\\u.exe\""), Roots));
        Assert.Null(AppFolder.Resolve(App(uninstall: $"\"{elsewhere}\\u.exe\""), Roots));
        Assert.Null(AppFolder.Resolve(App(uninstall: "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}"), Roots));
        Assert.Null(AppFolder.Resolve(App(), Roots));
    }
}
