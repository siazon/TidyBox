using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Catalog;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;
using PowerTools.Modules.ContextMenu.Tests.Support;
using PowerTools.Modules.ContextMenu.Toggle;

namespace PowerTools.Modules.ContextMenu.Tests.Toggle;

public class MenuTogglerTests : IDisposable
{
    private const string ClsidA = "{AAAAAAAA-0000-0000-0000-00000000000A}";
    private static readonly string RealDll = Path.Combine(Environment.SystemDirectory, "shell32.dll");

    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private sealed class PlainNames : IMenuNameResolver
    {
        public MenuNameInfo Resolve(MenuEntryKind kind, string keyName, string? clsid, string? modulePath, string? registeredName, string? muiVerb)
            => new(keyName.Trim(), null);
    }

    private MenuToggler Toggler() => new(_sandbox.Roots);

    private MenuEntry Reload(string id)
        => new ContextMenuCatalog(_sandbox.Roots, new PlainNames()).LoadAll().Single(e => e.Id == id);

    private MenuEntry AddHandler(string keyName, MenuLocation location = MenuLocation.AllFiles, string clsid = ClsidA)
    {
        _sandbox.AddHandler(Hive.Machine, location, keyName, clsid);
        _sandbox.RegisterClsid(Hive.Machine, clsid, keyName, RealDll);
        return Reload($"{location}|Handler|{keyName}");
    }

    private MenuEntry AddVerb(string verb, MenuLocation location = MenuLocation.Directory, Hive hive = Hive.Machine)
    {
        _sandbox.AddVerb(hive, location, verb, ("", verb));
        return Reload($"{location}|Verb|{verb}");
    }

    private static List<string> Snapshot(RegistryKey key, string path = "")
    {
        var lines = new List<string>();
        foreach (var name in key.GetValueNames()) lines.Add($"{path}|{name}|{key.GetValueKind(name)}|{key.GetValue(name)}");
        foreach (var sub in key.GetSubKeyNames())
        {
            lines.Add($"{path}\\{sub}");
            using var child = key.OpenSubKey(sub)!;
            lines.AddRange(Snapshot(child, $"{path}\\{sub}"));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private bool IsBlockedInUserList(string clsid)
    {
        using var key = _sandbox.Roots.User.OpenSubKey(ContextMenuRegistryRoots.BlockedPath);
        return key is not null && key.GetValueNames().Any(n => string.Equals(n, clsid, StringComparison.OrdinalIgnoreCase));
    }

    private RegistryKey? UserVerbKey(string location, string verb)
        => _sandbox.Roots.User.OpenSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\{location}\shell\{verb}");

    [Fact]
    public void SetEnabled_DisableHandler_AddsClsidToUserBlockedList_AndAsksForAnExplorerRestart()
    {
        var entry = AddHandler("WinRAR");

        var result = Toggler().SetEnabled(entry, enabled: false);

        Assert.True(result.Success);
        Assert.True(result.NeedsExplorerRestart);
        Assert.True(IsBlockedInUserList(ClsidA));
        Assert.Equal(MenuEntryState.Disabled, Reload(entry.Id).State);
    }

    [Fact]
    public void SetEnabled_EnableHandler_RemovesItFromTheUserBlockedList()
    {
        var entry = AddHandler("WinRAR");
        Toggler().SetEnabled(entry, enabled: false);

        var result = Toggler().SetEnabled(Reload(entry.Id), enabled: true);

        Assert.True(result.Success);
        Assert.True(result.NeedsExplorerRestart);
        Assert.False(IsBlockedInUserList(ClsidA));
        Assert.Equal(MenuEntryState.Enabled, Reload(entry.Id).State);
    }

    [Fact]
    public void SetEnabled_BlockedListSpellingDiffersInCase_IsMatchedAndNeverDuplicated()
    {
        var entry = AddHandler("WinRAR");
        _sandbox.Block(Hive.User, ClsidA.ToLowerInvariant());

        Toggler().SetEnabled(entry, enabled: false); // already blocked under another spelling: no second value
        using (var key = _sandbox.Roots.User.OpenSubKey(ContextMenuRegistryRoots.BlockedPath))
            Assert.Single(key!.GetValueNames());

        Toggler().SetEnabled(Reload(entry.Id), enabled: true); // and enabling removes that spelling
        Assert.False(IsBlockedInUserList(ClsidA));
    }

    [Fact]
    public void SetEnabled_HandlerKeyNameWithLeadingSpace_BlocksItsClsid()
    {
        var entry = AddHandler(" FileSyncEx");

        Assert.True(Toggler().SetEnabled(entry, enabled: false).Success);

        Assert.True(IsBlockedInUserList(ClsidA));
        Assert.Equal(MenuEntryState.Disabled, Reload("AllFiles|Handler| FileSyncEx").State);
    }

    [Fact]
    public void SetEnabled_SameClsidInTwoLocations_OneBlockDisablesBoth()
    {
        var inFiles = AddHandler("Sharing", MenuLocation.AllFiles);
        var inDirs = AddHandler("Sharing", MenuLocation.Directory);

        Toggler().SetEnabled(inFiles, enabled: false);

        Assert.Equal(MenuEntryState.Disabled, Reload(inFiles.Id).State);
        Assert.Equal(MenuEntryState.Disabled, Reload(inDirs.Id).State);
    }

    [Fact]
    public void SetEnabled_DisableVerb_WritesLegacyDisableInUserHive_AndLeavesMachineHiveUntouched()
    {
        var entry = AddVerb("git_shell");
        var machineBefore = Snapshot(_sandbox.Roots.Machine);

        var result = Toggler().SetEnabled(entry, enabled: false);

        Assert.True(result.Success);
        Assert.False(result.NeedsExplorerRestart); // verbs take effect immediately
        using (var key = UserVerbKey("Directory", "git_shell"))
        {
            Assert.NotNull(key!.GetValue("LegacyDisable"));
            Assert.Equal(new[] { "LegacyDisable" }, key.GetValueNames()); // only our value, nothing copied over
        }

        Assert.Equal(machineBefore, Snapshot(_sandbox.Roots.Machine));
        Assert.Equal(MenuEntryState.Disabled, Reload(entry.Id).State);
    }

    [Fact]
    public void SetEnabled_EnableVerb_RemovesTheValueAndDeletesTheEmptyOverrideKeyItCreated()
    {
        var entry = AddVerb("git_shell");
        Toggler().SetEnabled(entry, enabled: false);

        var result = Toggler().SetEnabled(Reload(entry.Id), enabled: true);

        Assert.True(result.Success);
        using (var key = UserVerbKey("Directory", "git_shell")) Assert.Null(key);
        Assert.Equal(MenuEntryState.Enabled, Reload(entry.Id).State);
    }

    [Fact]
    public void SetEnabled_EnableUserOwnedVerb_KeepsItsKey()
    {
        var entry = AddVerb("MyVerb", hive: Hive.User); // the user's own verb: no machine counterpart
        Toggler().SetEnabled(entry, enabled: false);

        Assert.True(Toggler().SetEnabled(Reload(entry.Id), enabled: true).Success);

        using var key = UserVerbKey("Directory", "MyVerb");
        Assert.NotNull(key);
        Assert.Null(key!.GetValue("LegacyDisable"));
        Assert.Equal("MyVerb", key.GetValue(""));
    }

    [Fact]
    public void SetEnabled_EnableVerbWhoseMachineDefinitionIsGone_DeletesTheLeftoverEmptyKey()
    {
        // e.g. we disabled git_shell, then Git was uninstalled: only our override key remains, holding just LegacyDisable.
        _sandbox.AddVerb(Hive.User, MenuLocation.Directory, "git_shell", ("LegacyDisable", ""));
        var entry = Reload("Directory|Verb|git_shell");
        Assert.Equal(MenuEntryState.Disabled, entry.State);
        Assert.False(entry.InMachineHive);

        Assert.True(Toggler().SetEnabled(entry, enabled: true).Success);

        using var key = UserVerbKey("Directory", "git_shell");
        Assert.Null(key); // an entirely empty key is not a verb anyone could use
    }

    [Fact]
    public void SetEnabled_AlreadyInTargetState_IsANoOpSuccessThatNeedsNoRestart()
    {
        var handler = AddHandler("WinRAR");
        var verb = AddVerb("git_shell");
        var before = Snapshot(_sandbox.Roots.User);

        var enableHandler = Toggler().SetEnabled(handler, enabled: true);
        var enableVerb = Toggler().SetEnabled(verb, enabled: true);

        Assert.True(enableHandler.Success && enableVerb.Success);
        Assert.False(enableHandler.NeedsExplorerRestart);
        Assert.Equal(before, Snapshot(_sandbox.Roots.User));

        Toggler().SetEnabled(handler, enabled: false);
        var disableAgain = Toggler().SetEnabled(Reload(handler.Id), enabled: false);
        Assert.True(disableAgain.Success);
        Assert.False(disableAgain.NeedsExplorerRestart);
    }

    [Fact]
    public void SetEnabled_EntryDisabledBySystem_FailsWithoutWriting()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "find", ("LegacyDisable", ""));
        var entry = Reload("Directory|Verb|find");
        var before = Snapshot(_sandbox.Roots.User);

        var enable = Toggler().SetEnabled(entry, enabled: true);
        var disable = Toggler().SetEnabled(entry, enabled: false);

        Assert.False(enable.Success);
        Assert.False(disable.Success);
        Assert.False(string.IsNullOrWhiteSpace(enable.Error));
        Assert.Equal(before, Snapshot(_sandbox.Roots.User));
    }

    [Fact]
    public void SetEnabled_CoreExplorerVerb_IsRefusedWithItsLockedReason_AndNothingIsWritten()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Folder, "open", ("", "Open"));
        var entry = Reload("Folder|Verb|open");
        Assert.NotNull(entry.LockedReason);
        var before = Snapshot(_sandbox.Roots.User);

        var result = Toggler().SetEnabled(entry, enabled: false);

        Assert.False(result.Success);
        Assert.Equal(entry.LockedReason, result.Error);
        Assert.Equal(before, Snapshot(_sandbox.Roots.User));
    }

    [Fact]
    public void SetEnabled_HandlerWithoutAClsid_Fails()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Mystery", "not a clsid");
        var entry = Reload("AllFiles|Handler|Mystery");

        var result = Toggler().SetEnabled(entry, enabled: false);

        Assert.False(result.Success);
        Assert.Contains("无法识别", result.Error);
    }

    [Fact]
    public void SetEnabled_HandlerWithAStaleRegistration_CanStillBeDisabled()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Folder, "WinRAR32", ClsidA); // CLSID never registered
        var entry = Reload("Folder|Handler|WinRAR32");
        Assert.NotNull(entry.Problem);

        Assert.True(Toggler().SetEnabled(entry, enabled: false).Success);
        Assert.True(IsBlockedInUserList(ClsidA));
    }

    [Fact]
    public void SetEnabled_NeverDeletesOrChangesAnythingInTheMachineHive()
    {
        var handler = AddHandler("WinRAR");
        var verb = AddVerb("git_shell");
        var machineBefore = Snapshot(_sandbox.Roots.Machine);

        Toggler().SetEnabled(handler, enabled: false);
        Toggler().SetEnabled(verb, enabled: false);
        Toggler().SetEnabled(Reload(handler.Id), enabled: true);
        Toggler().SetEnabled(Reload(verb.Id), enabled: true);

        Assert.Equal(machineBefore, Snapshot(_sandbox.Roots.Machine));
    }

    // A packaged app's own registry calls are virtualized and never reach the real registry; only a child process's do.
    // With a writer that changes nothing, an unchanged registry shows no write was made behind the writer's back.
    [Fact]
    public void SetEnabled_DisableHandlerAndVerb_AreWrittenOnlyThroughTheWriter()
    {
        var handler = AddHandler("WinRAR");
        var verb = AddVerb("git_shell");
        var before = Snapshot(_sandbox.Roots.User);
        var writer = new RecordingRegistryWriter();
        var toggler = new MenuToggler(_sandbox.Roots, writer);

        toggler.SetEnabled(handler, enabled: false);
        toggler.SetEnabled(verb, enabled: false);

        Assert.Equal(new[]
        {
            $@"SetString|{ContextMenuRegistryRoots.BlockedPath}|{ClsidA}|",
            $@"SetString|{ContextMenuRegistryRoots.ClassesPath}\Directory\shell\git_shell|LegacyDisable|",
        }, writer.Calls);
        Assert.Equal(before, Snapshot(_sandbox.Roots.User));
    }

    [Fact]
    public void SetEnabled_EnableHandler_RemovesTheBlockedValueThroughTheWriter()
    {
        var handler = AddHandler("WinRAR");
        _sandbox.Block(Hive.User, ClsidA.ToLowerInvariant());
        var before = Snapshot(_sandbox.Roots.User);
        var writer = new RecordingRegistryWriter();

        new MenuToggler(_sandbox.Roots, writer).SetEnabled(Reload(handler.Id), enabled: true);

        Assert.Equal(new[] { $@"DeleteValue|{ContextMenuRegistryRoots.BlockedPath}|{ClsidA.ToLowerInvariant()}" }, writer.Calls);
        Assert.Equal(before, Snapshot(_sandbox.Roots.User));
    }

    [Fact]
    public void SetEnabled_EnableVerbThatLeavesAnEmptyKey_DeletesTheValueThenTheKeyThroughTheWriter()
    {
        _sandbox.AddVerb(Hive.User, MenuLocation.Directory, "git_shell", ("LegacyDisable", ""));
        var entry = Reload("Directory|Verb|git_shell");
        var writer = new RecordingRegistryWriter(new RegExeRegistryWriter());

        new MenuToggler(_sandbox.Roots, writer).SetEnabled(entry, enabled: true);

        var key = $@"{ContextMenuRegistryRoots.ClassesPath}\Directory\shell\git_shell";
        Assert.Equal(new[] { $@"DeleteValue|{key}|LegacyDisable", $@"DeleteKey|{key}" }, writer.Calls);
    }

    [Fact]
    public void SetEnabled_RegistryRefusesTheWrite_ReturnsFailureInsteadOfThrowing()
    {
        var handler = AddHandler("WinRAR");
        var verb = AddVerb("git_shell");
        var me = WindowsIdentity.GetCurrent().User!;
        var rule = new RegistryAccessRule(me, RegistryRights.CreateSubKey | RegistryRights.SetValue, AccessControlType.Deny);
        var security = _sandbox.Hive(Hive.User).GetAccessControl();
        security.AddAccessRule(rule);
        _sandbox.Hive(Hive.User).SetAccessControl(security);
        try
        {
            var handlerResult = Toggler().SetEnabled(handler, enabled: false);
            var verbResult = Toggler().SetEnabled(verb, enabled: false);

            Assert.False(handlerResult.Success);
            Assert.False(verbResult.Success);
            Assert.False(string.IsNullOrWhiteSpace(handlerResult.Error));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            _sandbox.Hive(Hive.User).SetAccessControl(security);
        }
    }
}
