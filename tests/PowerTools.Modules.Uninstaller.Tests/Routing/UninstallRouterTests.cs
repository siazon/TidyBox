using System.ComponentModel;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Routing;
using PowerTools.Modules.Uninstaller.Tests.Support;

namespace PowerTools.Modules.Uninstaller.Tests.Routing;

public class UninstallRouterTests
{
    private readonly FakeRunner _runner = new();
    private readonly FakeStore _store = new();
    private readonly FakeUrls _urls = new();

    private UninstallRouter Router() => new(_runner, _store, _urls);

    private static InstalledApp App(AppSource source, string? uninstall = null, string? steamId = null, string? package = null)
        => new("id", "App", null, null, null, null, source, null, uninstall, steamId, package);

    [Fact]
    public async Task Steam_games_are_handed_to_steam_through_its_url_without_starting_a_process()
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Steam, steamId: "220"));

        Assert.Equal("steam://uninstall/220", _urls.Opened!.ToString());
        Assert.Null(_runner.Started);
        Assert.Equal(UninstallStatus.Completed, outcome.Status);
    }

    [Theory]
    [InlineData("22x")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_steam_app_id_that_is_not_all_digits_launches_nothing(string? appId)
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Steam, steamId: appId));

        Assert.Equal(UninstallStatus.NotAvailable, outcome.Status);
        Assert.Null(_urls.Opened);
    }

    [Fact]
    public async Task Win32_exit_code_zero_is_completed()
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\" /S"));

        Assert.Equal(("C:\\a\\unins.exe", "/S"), _runner.Started);
        Assert.Equal(UninstallStatus.Completed, outcome.Status);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public async Task Win32_non_zero_exit_code_is_failed_with_the_code()
    {
        _runner.Result = () => 5;

        var outcome = await Router().UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\""));

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.Equal(5, outcome.ExitCode);
    }

    [Fact]
    public async Task Win32_reboot_required_exit_codes_still_count_as_completed()
    {
        _runner.Result = () => 3010;

        var outcome = await Router().UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\""));

        Assert.Equal(UninstallStatus.Completed, outcome.Status);
        Assert.Equal(3010, outcome.ExitCode);
    }

    [Fact]
    public async Task Cancelling_the_uac_prompt_is_reported_as_cancelled()
    {
        var router = new UninstallRouter(new ThrowingRunner(new Win32Exception(1223)), _store, _urls);

        var outcome = await router.UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\""));

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.Contains("取消", outcome.Message);
    }

    [Fact]
    public async Task A_missing_uninstall_program_is_failed_not_thrown()
    {
        var router = new UninstallRouter(new ThrowingRunner(new Win32Exception(2)), _store, _urls);

        var outcome = await router.UninstallAsync(App(AppSource.Win32, "\"C:\\gone\\unins.exe\""));

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Win32_without_a_usable_uninstall_string_is_not_available(string? uninstall)
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Win32, uninstall));

        Assert.Equal(UninstallStatus.NotAvailable, outcome.Status);
        Assert.Null(_runner.Started);
    }

    [Theory]
    [InlineData("MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}")]
    [InlineData("\"C:\\Users\\u\\AppData\\Local\\Postman\\Update.exe\" --uninstall")]
    public async Task Launchers_that_hand_over_to_a_child_process_may_still_be_running_after_exit_zero(string uninstall)
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Win32, uninstall));

        Assert.Equal(UninstallStatus.MaybeStillRunning, outcome.Status);
    }

    [Fact]
    public async Task Store_apps_are_removed_by_package_full_name()
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Store, package: "Pkg_1.0_x64__abc"));

        Assert.Equal("Pkg_1.0_x64__abc", _store.Removed);
        Assert.Equal(UninstallStatus.Completed, outcome.Status);
    }

    [Fact]
    public async Task A_store_removal_error_is_failed_with_its_message()
    {
        _store.Throw = new InvalidOperationException("denied");

        var outcome = await Router().UninstallAsync(App(AppSource.Store, package: "Pkg_1.0_x64__abc"));

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.Contains("denied", outcome.Message);
    }

    private sealed class ThrowingRunner(Exception ex) : IProcessRunner
    {
        public Task<int> RunAndWaitAsync(string fileName, string arguments, CancellationToken ct) => throw ex;
    }
}
