using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Tests.Support;

namespace TidyBox.Modules.ContextMenu.Tests.Hives;

/// <summary>These run the real reg.exe against a sandbox under HKCU, the same way the packaged app does.</summary>
public class RegExeRegistryWriterTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();
    private readonly RegExeRegistryWriter _writer = new();

    public void Dispose() => _sandbox.Dispose();

    private RegistryKey User => _sandbox.Roots.User;

    [Fact]
    public void SetString_creates_the_key_and_a_named_string_value()
    {
        _writer.SetString(User, @"Software\Classes\Directory\shell\git", "LegacyDisable", "");

        using var key = User.OpenSubKey(@"Software\Classes\Directory\shell\git");
        Assert.Equal("", key!.GetValue("LegacyDisable"));
        Assert.Equal(RegistryValueKind.String, key.GetValueKind("LegacyDisable"));
    }

    [Fact]
    public void SetString_with_no_name_writes_the_default_value()
    {
        _writer.SetString(User, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", null, "");

        using var key = User.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
        Assert.Equal("", key!.GetValue(""));
        Assert.Equal(RegistryValueKind.String, key.GetValueKind(""));
    }

    [Fact]
    public void SetString_overwrites_an_existing_value_and_keeps_the_others()
    {
        _writer.SetString(User, @"Software\X", "a", "1");
        _writer.SetString(User, @"Software\X", "b", "2");

        _writer.SetString(User, @"Software\X", "a", "3");

        using var key = User.OpenSubKey(@"Software\X");
        Assert.Equal("3", key!.GetValue("a"));
        Assert.Equal("2", key.GetValue("b"));
    }

    [Fact]
    public void Names_with_spaces_and_shell_characters_are_passed_through_untouched()
    {
        _writer.SetString(User, @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", "{AAAAAAAA-0000-0000-0000-00000000000A}", "");
        _writer.SetString(User, @"Software\Classes\Directory\shell\A & B ^ %PATH%", "LegacyDisable", "");

        using var blocked = User.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
        Assert.NotNull(blocked!.GetValue("{AAAAAAAA-0000-0000-0000-00000000000A}"));
        using var odd = User.OpenSubKey(@"Software\Classes\Directory\shell\A & B ^ %PATH%");
        Assert.NotNull(odd!.GetValue("LegacyDisable"));
    }

    [Fact]
    public void DeleteValue_removes_only_that_value()
    {
        _writer.SetString(User, @"Software\X", "a", "1");
        _writer.SetString(User, @"Software\X", "b", "2");

        _writer.DeleteValue(User, @"Software\X", "a");

        using var key = User.OpenSubKey(@"Software\X");
        Assert.Equal(new[] { "b" }, key!.GetValueNames());
    }

    [Fact]
    public void DeleteValue_of_something_that_is_not_there_is_not_an_error()
    {
        _writer.DeleteValue(User, @"Software\NoSuchKey", "a");
        _writer.SetString(User, @"Software\X", "b", "2");

        _writer.DeleteValue(User, @"Software\X", "missing");

        using var key = User.OpenSubKey(@"Software\X");
        Assert.Equal(new[] { "b" }, key!.GetValueNames());
    }

    [Fact]
    public void DeleteKey_removes_the_key_with_everything_under_it_but_not_its_siblings()
    {
        _writer.SetString(User, @"Software\Classes\CLSID\{A}\InprocServer32", null, "");
        _writer.SetString(User, @"Software\Classes\CLSID\{B}", "keep", "1");

        _writer.DeleteKey(User, @"Software\Classes\CLSID\{A}");

        using (var gone = User.OpenSubKey(@"Software\Classes\CLSID\{A}")) Assert.Null(gone);
        using (var sibling = User.OpenSubKey(@"Software\Classes\CLSID\{B}")) Assert.NotNull(sibling);
    }

    [Fact]
    public void DeleteKey_of_a_key_that_is_not_there_is_not_an_error()
    {
        _writer.DeleteKey(User, @"Software\Classes\CLSID\{NoSuchKey}");
    }

    [Fact]
    public void A_write_the_registry_refuses_is_an_IOException()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        var rule = new RegistryAccessRule(me, RegistryRights.CreateSubKey | RegistryRights.SetValue, AccessControlType.Deny);
        var security = _sandbox.Hive(Hive.User).GetAccessControl();
        security.AddAccessRule(rule);
        _sandbox.Hive(Hive.User).SetAccessControl(security);
        try
        {
            Assert.Throws<IOException>(() => _writer.SetString(User, @"Software\X", "a", "1"));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            _sandbox.Hive(Hive.User).SetAccessControl(security);
        }
    }
}
