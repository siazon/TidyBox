using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Classic;
using PowerTools.Modules.ContextMenu.Tests.Support;

namespace PowerTools.Modules.ContextMenu.Tests.Classic;

public class ClassicMenuSwitchTests : IDisposable
{
    private const string SwitchKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private ClassicMenuSwitch Switch() => new(_sandbox.Roots);

    private void WriteDefault(string value)
    {
        using var key = _sandbox.Roots.User.CreateSubKey(SwitchKey + @"\InprocServer32");
        key.SetValue("", value, RegistryValueKind.String);
    }

    [Fact]
    public void IsEnabled_NothingWritten_IsFalse()
    {
        Assert.False(Switch().IsEnabled);
    }

    [Fact]
    public void SetEnabled_True_CreatesInprocServer32WithAnEmptyStringDefault_AndAsksForARestart()
    {
        var result = Switch().SetEnabled(true);

        Assert.True(result.Success);
        Assert.True(result.NeedsExplorerRestart);
        Assert.True(Switch().IsEnabled);
        using var key = _sandbox.Roots.User.OpenSubKey(SwitchKey + @"\InprocServer32");
        Assert.Equal("", key!.GetValue(""));
        Assert.Equal(RegistryValueKind.String, key.GetValueKind(""));
    }

    [Fact]
    public void SetEnabled_True_NeverTouchesTheMachineHive()
    {
        Switch().SetEnabled(true);

        using var key = _sandbox.Roots.Machine.OpenSubKey(@"Software\Classes\CLSID");
        Assert.Null(key);
    }

    [Fact]
    public void SetEnabled_False_RemovesOnlyTheSwitchKey()
    {
        _sandbox.RegisterClsid(Hive.User, "{AAAAAAAA-0000-0000-0000-00000000000A}", "Someone else's", null);
        Switch().SetEnabled(true);

        var result = Switch().SetEnabled(false);

        Assert.True(result.Success);
        Assert.True(result.NeedsExplorerRestart);
        Assert.False(Switch().IsEnabled);
        using (var gone = _sandbox.Roots.User.OpenSubKey(SwitchKey)) Assert.Null(gone);
        using (var other = _sandbox.Roots.User.OpenSubKey(@"Software\Classes\CLSID\{AAAAAAAA-0000-0000-0000-00000000000A}")) Assert.NotNull(other);
    }

    [Theory]
    [InlineData("/f")]            // what a PowerShell 5.1 "reg add ... /d """ mishap writes
    [InlineData("C:\\real.dll")]  // a real server registration is not our switch
    public void IsEnabled_KeyExistsButDefaultIsNotAnEmptyString_IsFalse(string wrongDefault)
    {
        WriteDefault(wrongDefault);

        Assert.False(Switch().IsEnabled);
    }

    [Fact]
    public void SetEnabled_True_RepairsAWrongDefaultValue()
    {
        WriteDefault("/f");

        var result = Switch().SetEnabled(true);

        Assert.True(result.Success);
        Assert.True(Switch().IsEnabled);
    }

    [Fact]
    public void SetEnabled_AlreadyInTheRequestedState_IsANoOpThatNeedsNoRestart()
    {
        var off = Switch().SetEnabled(false);
        Switch().SetEnabled(true);
        var on = Switch().SetEnabled(true);

        Assert.True(off.Success);
        Assert.False(off.NeedsExplorerRestart);
        Assert.True(on.Success);
        Assert.False(on.NeedsExplorerRestart);
    }

    [Fact]
    public void SetEnabled_RegistryRefusesTheWrite_ReturnsFailureInsteadOfThrowing()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        var rule = new RegistryAccessRule(me, RegistryRights.CreateSubKey | RegistryRights.SetValue, AccessControlType.Deny);
        var security = _sandbox.Hive(Hive.User).GetAccessControl();
        security.AddAccessRule(rule);
        _sandbox.Hive(Hive.User).SetAccessControl(security);
        try
        {
            var result = Switch().SetEnabled(true);

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
