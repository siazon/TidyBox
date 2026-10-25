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
        Assert.False(outcome.NeedsAdministrator);
    }

    [Fact]
    public async Task An_uninstaller_the_user_may_not_run_asks_for_an_administrator_retry()
    {
        var router = new UninstallRouter(new ThrowingRunner(new Win32Exception(5)), _store, _urls);

        var outcome = await router.UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\""));

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.True(outcome.NeedsAdministrator);
    }

    // An uninstaller that only administrators may execute cannot be started even by "run as administrator" (the shell
    // checks the file first), so the elevated command prompt starts it instead.
    [Fact]
    public async Task As_administrator_the_uninstaller_is_started_by_an_elevated_command_prompt()
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Win32, "\"C:\\a b\\unins.exe\" /S"), asAdministrator: true);

        Assert.Equal(("cmd.exe", "/c \"\"C:\\a b\\unins.exe\" /S\""), _runner.Started);
        Assert.True(_runner.StartedAsAdministrator);
        Assert.Equal(UninstallStatus.Completed, outcome.Status);
    }

    [Fact]
    public async Task Without_the_flag_the_uninstaller_is_started_directly_and_not_elevated()
    {
        await Router().UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\" /S"));

        Assert.Equal(("C:\\a\\unins.exe", "/S"), _runner.Started);
        Assert.False(_runner.StartedAsAdministrator);
    }

    [Theory]
    [InlineData("\"C:\\a\\unins.exe\" /S & calc")]
    [InlineData("\"C:\\a\\unins.exe\" /S | calc")]
    [InlineData("\"C:\\a\\unins.exe\" > C:\\x.txt")]
    [InlineData("\"C:\\a\\unins.exe\" ^& calc")]
    [InlineData("\"C:\\a\\unins.exe\" /x \"%NOSUCHVARIABLE%\\y\"")] // the parser leaves unknown variables alone, and cmd would expand them
    [InlineData("\"C:\\a&b\\unins.exe\"")]
    public async Task As_administrator_a_command_the_shell_could_split_is_not_run(string uninstall)
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Win32, uninstall), asAdministrator: true);

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.False(outcome.NeedsAdministrator);
        Assert.Null(_runner.Started);
    }

    [Fact]
    public async Task As_administrator_a_refusal_to_start_does_not_ask_again()
    {
        var router = new UninstallRouter(new ThrowingRunner(new Win32Exception(5)), _store, _urls);

        var outcome = await router.UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\""), asAdministrator: true);

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.False(outcome.NeedsAdministrator);
    }

    [Fact]
    public async Task As_administrator_a_handing_over_launcher_is_still_recognised_by_its_own_name()
    {
        var outcome = await Router().UninstallAsync(App(AppSource.Win32, "MsiExec.exe /X{AAAAAAAA-0000-0000-0000-000000000000}"), asAdministrator: true);

        Assert.Equal(UninstallStatus.MaybeStillRunning, outcome.Status);
    }

    [Fact]
    public async Task As_administrator_the_exit_code_of_the_uninstaller_still_decides_the_outcome()
    {
        _runner.Result = () => 5;

        var outcome = await Router().UninstallAsync(App(AppSource.Win32, "\"C:\\a\\unins.exe\""), asAdministrator: true);

        Assert.Equal(UninstallStatus.Failed, outcome.Status);
        Assert.Equal(5, outcome.ExitCode);
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
        public Task<int> RunAndWaitAsync(string fileName, string arguments, CancellationToken ct, bool asAdministrator = false) => throw ex;
    }
}
