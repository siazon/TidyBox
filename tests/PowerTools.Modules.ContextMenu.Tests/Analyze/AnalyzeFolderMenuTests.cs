using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Analyze;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Tests.Support;

namespace PowerTools.Modules.ContextMenu.Tests.Analyze;

public class AnalyzeFolderMenuTests : IDisposable
{
    private const string Exe = @"C:\Users\Jo Smith\AppData\Local\Microsoft\WindowsApps\PowerTools.exe";
    private const string FolderVerb = @"Software\Classes\Directory\shell\PowerTools.Analyze";
    private const string BackgroundVerb = @"Software\Classes\Directory\Background\shell\PowerTools.Analyze";

    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private AnalyzeFolderMenu Menu(string exe = Exe) => new(_sandbox.Roots, exe);

    private string? Default(string key)
    {
        using var k = _sandbox.Roots.User.OpenSubKey(key);
        return k?.GetValue("") as string;
    }

    [Fact]
    public void Nothing_is_added_until_the_user_asks()
    {
        Assert.False(Menu().IsEnabled);
    }

    // %1 is the folder that was right-clicked, %V the folder whose empty space was right-clicked. The "\." keeps a drive
    // root (D:\) from ending in a backslash before the closing quote.
    [Fact]
    public void Enabling_adds_a_verb_for_a_selected_folder_and_one_for_the_empty_space_inside_a_folder()
    {
        var result = Menu().SetEnabled(true);

        Assert.True(result.Success);
        Assert.False(result.NeedsExplorerRestart);
        Assert.True(Menu().IsEnabled);
        Assert.Equal("分析此文件夹（PowerTools）", Default(FolderVerb));
        Assert.Equal("分析此文件夹（PowerTools）", Default(BackgroundVerb));
        Assert.Equal($"\"{Exe}\" --analyze \"%1\\.\"", Default(FolderVerb + @"\command"));
        Assert.Equal($"\"{Exe}\" --analyze \"%V\\.\"", Default(BackgroundVerb + @"\command"));
    }

    [Fact]
    public void Enabling_never_touches_the_machine_hive()
    {
        Menu().SetEnabled(true);

        using var key = _sandbox.Roots.Machine.OpenSubKey("Software");
        Assert.Null(key);
    }

    [Fact]
    public void Disabling_removes_both_verbs_and_only_those()
    {
        _sandbox.AddVerb(Hive.User, Model.MenuLocation.Directory, "other", ("", "Other"));
        Menu().SetEnabled(true);

        var result = Menu().SetEnabled(false);

        Assert.True(result.Success);
        Assert.False(Menu().IsEnabled);
        using (var folder = _sandbox.Roots.User.OpenSubKey(FolderVerb)) Assert.Null(folder);
        using (var background = _sandbox.Roots.User.OpenSubKey(BackgroundVerb)) Assert.Null(background);
        using (var other = _sandbox.Roots.User.OpenSubKey(@"Software\Classes\Directory\shell\other")) Assert.NotNull(other);
    }

    [Fact]
    public void Disabling_when_nothing_was_added_is_a_success()
    {
        Assert.True(Menu().SetEnabled(false).Success);
    }

    [Fact]
    public void Enabling_twice_is_the_same_as_once()
    {
        Menu().SetEnabled(true);

        Assert.True(Menu().SetEnabled(true).Success);
        Assert.True(Menu().IsEnabled);
    }

    [Fact]
    public void A_menu_that_points_at_a_program_that_moved_counts_as_off_and_enabling_repairs_it()
    {
        Menu(@"C:\Old\PowerTools.exe").SetEnabled(true);

        Assert.False(Menu().IsEnabled);

        Assert.True(Menu().SetEnabled(true).Success);
        Assert.True(Menu().IsEnabled);
        Assert.Equal($"\"{Exe}\" --analyze \"%1\\.\"", Default(FolderVerb + @"\command"));
    }

    [Fact]
    public void Only_one_of_the_two_verbs_present_does_not_count_as_on()
    {
        Menu().SetEnabled(true);
        new RegExeRegistryWriter().DeleteKey(_sandbox.Roots.User, BackgroundVerb);

        Assert.False(Menu().IsEnabled);
    }

    private const string IconA = @"C:\Apps\PowerTools_1.0.0.0\Assets\PowerTools.ico";
    private const string IconB = @"C:\Apps\PowerTools_1.0.1.0\Assets\PowerTools.ico";

    private AnalyzeFolderMenu WithIcon(string icon, IRegistryWriter? writer = null) => new(_sandbox.Roots, Exe, writer, icon);

    private string? Named(string key, string name)
    {
        using var k = _sandbox.Roots.User.OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    [Fact]
    public void With_an_icon_both_verbs_carry_it()
    {
        WithIcon(IconA).SetEnabled(true);

        Assert.Equal(IconA, Named(FolderVerb, "Icon"));
        Assert.Equal(IconA, Named(BackgroundVerb, "Icon"));
    }

    [Fact]
    public void Without_an_icon_no_icon_value_is_written()
    {
        Menu().SetEnabled(true);

        Assert.Null(Named(FolderVerb, "Icon"));
    }

    [Fact]
    public void Refreshing_points_an_enabled_menu_at_the_current_icon()
    {
        WithIcon(IconA).SetEnabled(true);

        var changed = WithIcon(IconB).RefreshIcon();

        Assert.True(changed);
        Assert.Equal(IconB, Named(FolderVerb, "Icon"));
        Assert.Equal(IconB, Named(BackgroundVerb, "Icon"));
        Assert.True(WithIcon(IconB).IsEnabled);
    }

    [Fact]
    public void Refreshing_a_menu_that_is_off_creates_nothing()
    {
        var changed = WithIcon(IconB).RefreshIcon();

        Assert.False(changed);
        using var key = _sandbox.Roots.User.OpenSubKey("Software");
        Assert.Null(key);
    }

    [Fact]
    public void Refreshing_when_the_icon_is_already_current_writes_nothing()
    {
        WithIcon(IconA).SetEnabled(true);
        var writer = new RecordingRegistryWriter();

        var changed = WithIcon(IconA, writer).RefreshIcon();

        Assert.False(changed);
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Refreshing_without_an_icon_to_offer_does_nothing()
    {
        WithIcon(IconA).SetEnabled(true);

        Assert.False(Menu().RefreshIcon());
        Assert.Equal(IconA, Named(FolderVerb, "Icon"));
    }

    [Fact]
    public void Everything_is_written_through_the_registry_writer()
    {
        var writer = new RecordingRegistryWriter();

        new AnalyzeFolderMenu(_sandbox.Roots, Exe, writer).SetEnabled(true);

        Assert.Equal(new[]
        {
            $"SetString|{FolderVerb}|(default)|分析此文件夹（PowerTools）",
            $"SetString|{FolderVerb}\\command|(default)|\"{Exe}\" --analyze \"%1\\.\"",
            $"SetString|{BackgroundVerb}|(default)|分析此文件夹（PowerTools）",
            $"SetString|{BackgroundVerb}\\command|(default)|\"{Exe}\" --analyze \"%V\\.\"",
        }, writer.Calls);
        using var key = _sandbox.Roots.User.OpenSubKey("Software");
        Assert.Null(key); // nothing was written behind the writer's back
    }

    [Fact]
    public void A_registry_that_refuses_the_write_is_a_failure_not_an_exception()
    {
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
