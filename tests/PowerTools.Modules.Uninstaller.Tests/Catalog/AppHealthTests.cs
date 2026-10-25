using PowerTools.Modules.Uninstaller.Catalog;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Tests.Catalog;

public sealed class AppHealthTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));

    public AppHealthTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private static InstalledApp App(AppSource source = AppSource.Win32, string? installLocation = null, string? uninstall = null)
        => new("id", "App", null, null, null, null, source, installLocation, uninstall, null, null);

    [Fact]
    public void A_program_with_an_existing_uninstaller_is_healthy()
    {
        var exe = Path.Combine(_base, "unins000.exe");
        File.WriteAllText(exe, "");

        Assert.Null(AppHealth.Check(App(installLocation: _base, uninstall: $"\"{exe}\" /S")));
    }

    [Fact]
    public void A_missing_uninstaller_file_is_reported()
    {
        var problem = AppHealth.Check(App(uninstall: $"\"{Path.Combine(_base, "gone.exe")}\" /S"));

        Assert.Equal("卸载程序已丢失", problem);
    }

    [Fact]
    public void No_uninstall_command_is_reported()
    {
        Assert.Equal("没有登记卸载命令", AppHealth.Check(App(uninstall: null)));
        Assert.Equal("没有登记卸载命令", AppHealth.Check(App(uninstall: "  ")));
    }

    [Fact]
    public void A_missing_install_folder_is_reported_when_the_uninstaller_exists()
    {
        var exe = Path.Combine(_base, "u.exe");
        File.WriteAllText(exe, "");

        var problem = AppHealth.Check(App(installLocation: Path.Combine(_base, "gone"), uninstall: $"\"{exe}\""));

        Assert.Equal("安装目录已不存在", problem);
    }

    [Fact]
    public void System_programs_found_by_name_only_count_as_present()
    {
        Assert.Null(AppHealth.Check(App(uninstall: "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}")));
    }

    [Fact]
    public void Steam_and_Store_entries_are_never_flagged()
    {
        Assert.Null(AppHealth.Check(App(AppSource.Steam)));
        Assert.Null(AppHealth.Check(App(AppSource.Store)));
    }
}
