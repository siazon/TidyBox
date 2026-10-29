using Microsoft.Win32;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Model;
using TidyBox.Modules.ContextMenu.Tests.Support;

namespace TidyBox.Modules.ContextMenu.Tests.Hives;

public class ContextMenuRegistryRootsTests
{
    [Theory]
    [InlineData(MenuLocation.AllFiles, "*", "所有文件")]
    [InlineData(MenuLocation.AllFileSystemObjects, "AllFilesystemObjects", "所有文件和文件夹")]
    [InlineData(MenuLocation.Directory, "Directory", "文件夹")]
    [InlineData(MenuLocation.DirectoryBackground, @"Directory\Background", "文件夹空白处")]
    [InlineData(MenuLocation.Folder, "Folder", "文件夹和库")]
    [InlineData(MenuLocation.Drive, "Drive", "驱动器")]
    public void MenuLocation_MapsToItsRegistryKeyAndChineseName(MenuLocation location, string keyName, string displayName)
    {
        Assert.Equal(keyName, location.RegistryKeyName());
        Assert.Equal(displayName, location.DisplayName());
    }

    [Fact]
    public void RegistryPathConstants_MatchTheShellLocations()
    {
        Assert.Equal(@"Software\Classes", ContextMenuRegistryRoots.ClassesPath);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", ContextMenuRegistryRoots.BlockedPath);
    }

    [Fact]
    public void Real_UsesTheActualMachineAndUserHives()
    {
        var roots = ContextMenuRegistryRoots.Real();

        Assert.Equal("HKEY_LOCAL_MACHINE", roots.Machine.Name);
        Assert.Equal("HKEY_CURRENT_USER", roots.User.Name);
    }

    [Fact]
    public void Real_AlwaysUsesTheNative64BitRegistryView_SoA32BitAppStillSpeaksToExplorersRegistry()
    {
        // Explorer is 64-bit. A 32-bit process (VS defaults to an x86 package) that used the default view would read and
        // write HKCU\Software\Classes\Wow6432Node\... instead, and its changes would never reach Explorer.
        var roots = ContextMenuRegistryRoots.Real();

        Assert.Equal(RegistryView.Registry64, roots.Machine.View);
        Assert.Equal(RegistryView.Registry64, roots.User.View);
    }

    [Fact]
    public void Sandbox_MachineAndUserHivesAreIndependent()
    {
        using var sandbox = new RegistrySandbox();

        sandbox.AddHandler(Hive.User, MenuLocation.AllFiles, "OnlyInUser", "{AAAAAAAA-0000-0000-0000-000000000001}");

        var path = $@"{ContextMenuRegistryRoots.ClassesPath}\*\shellex\ContextMenuHandlers\OnlyInUser";
        using (var user = sandbox.Roots.User.OpenSubKey(path)) Assert.NotNull(user);
        using (var machine = sandbox.Roots.Machine.OpenSubKey(path)) Assert.Null(machine);
    }

    [Fact]
    public void Sandbox_HelpersWriteTheShapesTheShellUses()
    {
        using var sandbox = new RegistrySandbox();
        const string clsid = "{AAAAAAAA-0000-0000-0000-000000000002}";

        sandbox.AddHandler(Hive.Machine, MenuLocation.DirectoryBackground, " Spaced", clsid);
        sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "cmd", ("MUIVerb", "Open here"), ("LegacyDisable", ""));
        sandbox.RegisterClsid(Hive.Machine, clsid, "My Handler", @"C:\x\y.dll");
        sandbox.Block(Hive.User, clsid);

        using (var handler = sandbox.Roots.Machine.OpenSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\Directory\Background\shellex\ContextMenuHandlers\ Spaced"))
            Assert.Equal(clsid, handler!.GetValue(""));
        using (var verb = sandbox.Roots.Machine.OpenSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\Directory\shell\cmd"))
        {
            Assert.Equal("Open here", verb!.GetValue("MUIVerb"));
            Assert.NotNull(verb.GetValue("LegacyDisable"));
        }
        using (var server = sandbox.Roots.Machine.OpenSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\CLSID\{clsid}\InprocServer32"))
            Assert.Equal(@"C:\x\y.dll", server!.GetValue(""));
        using (var blocked = sandbox.Roots.User.OpenSubKey(ContextMenuRegistryRoots.BlockedPath))
            Assert.Contains(clsid, blocked!.GetValueNames());
    }

    [Fact]
    public void Sandbox_Dispose_RemovesEverythingItCreated()
    {
        string path;
        using (var sandbox = new RegistrySandbox())
        {
            path = sandbox.SandboxPath;
            sandbox.AddHandler(Hive.User, MenuLocation.Drive, "X", "{AAAAAAAA-0000-0000-0000-000000000003}");
            using var present = Registry.CurrentUser.OpenSubKey(path);
            Assert.NotNull(present);
        }

        using var gone = Registry.CurrentUser.OpenSubKey(path);
        Assert.Null(gone);
    }
}
