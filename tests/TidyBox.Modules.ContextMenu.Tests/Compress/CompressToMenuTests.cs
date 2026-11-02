using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using TidyBox.Modules.ContextMenu.Compress;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Tests.Support;

namespace TidyBox.Modules.ContextMenu.Tests.Compress;

public class CompressToMenuTests : IDisposable
{
    private const string Verb = @"Software\Classes\AllFilesystemObjects\shell\TidyBox.CompressTo";
    private const string CommandStore = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";

    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private CompressToMenu Menu(IRegistryWriter? writer = null) => new(_sandbox.Roots, writer);

    // What a Windows 11 machine has: the commands the new menu's "压缩到" is made of.
    private void SystemHasCommands(params string[] names)
    {
        foreach (var name in names) _sandbox.Roots.Machine.CreateSubKey($@"{CommandStore}\Windows.CompressTo.{name}").Dispose();
    }

    private string? Named(string key, string name)
    {
        using var k = _sandbox.Roots.User.OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    [Fact]
    public void Nothing_is_added_until_the_user_asks()
    {
        SystemHasCommands("Zip");

        Assert.False(Menu().IsEnabled);
    }

    [Fact]
    public void The_menu_is_unavailable_on_a_machine_without_the_system_commands()
    {
        Assert.False(Menu().IsAvailable);
    }

    [Fact]
    public void The_menu_is_available_when_the_system_has_any_of_the_commands()
    {
        SystemHasCommands("Tar");

        Assert.True(Menu().IsAvailable);
    }

    [Fact]
    public void Enabling_adds_a_submenu_made_of_the_system_commands_in_menu_order()
    {
        SystemHasCommands("Wizard", "Tar", "Zip", "7Z");

        var result = Menu().SetEnabled(true);

        Assert.True(result.Success);
        Assert.False(result.NeedsExplorerRestart);
        Assert.True(Menu().IsEnabled);
        Assert.Equal("压缩到", Named(Verb, "MUIVerb"));
        Assert.Equal("Windows.CompressTo.Zip;Windows.CompressTo.7Z;Windows.CompressTo.Tar;Windows.CompressTo.Wizard", Named(Verb, "SubCommands"));
    }

    [Fact]
    public void Only_the_commands_this_system_has_are_listed()
    {
        SystemHasCommands("Zip", "Tar");

        Menu().SetEnabled(true);

        Assert.Equal("Windows.CompressTo.Zip;Windows.CompressTo.Tar", Named(Verb, "SubCommands"));
    }

    [Fact]
    public void Enabling_without_the_system_commands_fails_with_a_reason_and_writes_nothing()
    {
        var writer = new RecordingRegistryWriter();

        var result = Menu(writer).SetEnabled(true);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Enabling_never_writes_to_the_machine_hive()
    {
        SystemHasCommands("Zip");

        Menu().SetEnabled(true);

        using var classes = _sandbox.Roots.Machine.OpenSubKey(@"Software\Classes");
        Assert.Null(classes);
    }

    [Fact]
    public void Disabling_removes_only_our_verb()
    {
        SystemHasCommands("Zip");
        _sandbox.AddVerb(Hive.User, Model.MenuLocation.AllFileSystemObjects, "other", ("", "Other"));
        Menu().SetEnabled(true);

        var result = Menu().SetEnabled(false);

        Assert.True(result.Success);
        Assert.False(Menu().IsEnabled);
        using (var ours = _sandbox.Roots.User.OpenSubKey(Verb)) Assert.Null(ours);
        using (var other = _sandbox.Roots.User.OpenSubKey(@"Software\Classes\AllFilesystemObjects\shell\other")) Assert.NotNull(other);
    }

    [Fact]
    public void Disabling_when_nothing_was_added_is_a_success_even_without_the_system_commands()
    {
        Assert.True(Menu().SetEnabled(false).Success);
    }

    [Fact]
    public void Enabling_twice_is_the_same_as_once()
    {
        SystemHasCommands("Zip");
        Menu().SetEnabled(true);

        Assert.True(Menu().SetEnabled(true).Success);
        Assert.True(Menu().IsEnabled);
    }

    // A list written when Windows had more commands (or by hand) no longer matches what the system offers: that is off, and enabling repairs it.
    [Fact]
    public void A_submenu_that_differs_from_what_the_system_offers_counts_as_off_and_enabling_repairs_it()
    {
        SystemHasCommands("Zip");
        _sandbox.AddVerb(Hive.User, Model.MenuLocation.AllFileSystemObjects, "TidyBox.CompressTo", ("MUIVerb", "压缩到"), ("SubCommands", "Windows.CompressTo.Zip;Windows.CompressTo.Gone"));

        Assert.False(Menu().IsEnabled);

        Assert.True(Menu().SetEnabled(true).Success);
        Assert.True(Menu().IsEnabled);
        Assert.Equal("Windows.CompressTo.Zip", Named(Verb, "SubCommands"));
    }

    [Fact]
    public void Everything_is_written_through_the_registry_writer()
    {
        SystemHasCommands("Zip", "7Z");
        var writer = new RecordingRegistryWriter();

        Menu(writer).SetEnabled(true);

        Assert.Equal(new[]
        {
            $"SetString|{Verb}|MUIVerb|压缩到",
            $"SetString|{Verb}|SubCommands|Windows.CompressTo.Zip;Windows.CompressTo.7Z",
        }, writer.Calls);
        using var key = _sandbox.Roots.User.OpenSubKey("Software");
        Assert.Null(key); // nothing was written behind the writer's back
    }

    [Fact]
    public void A_registry_that_refuses_the_write_is_a_failure_not_an_exception()
    {
        SystemHasCommands("Zip");
        var me = WindowsIdentity.GetCurrent().User!;
        var rule = new RegistryAccessRule(me, RegistryRights.CreateSubKey | RegistryRights.SetValue, AccessControlType.Deny);
        var security = _sandbox.Hive(Hive.User).GetAccessControl();
        security.AddAccessRule(rule);
        _sandbox.Hive(Hive.User).SetAccessControl(security);
        try
        {
            var result = Menu().SetEnabled(true);

            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            _sandbox.Hive(Hive.User).SetAccessControl(security);
        }
    }
}
