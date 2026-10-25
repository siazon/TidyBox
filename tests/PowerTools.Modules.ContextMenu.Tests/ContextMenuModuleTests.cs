using Microsoft.Win32;
using PowerTools.Core.Modules;
using PowerTools.Modules.ContextMenu.Classic;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;
using PowerTools.Modules.ContextMenu.Tests.Support;

namespace PowerTools.Modules.ContextMenu.Tests;

public class ContextMenuModuleTests : IDisposable
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

    private sealed class CountingRestarter : IExplorerRestarter
    {
        public int Restarts { get; private set; }

        public Task RestartAsync(CancellationToken ct = default)
        {
            Restarts++;
            return Task.CompletedTask;
        }
    }

    private ContextMenuModule NewModule(IExplorerRestarter? restarter = null)
        => new(_sandbox.Roots, new PlainNames(), restarter ?? new CountingRestarter());

    [Fact]
    public void ModuleId_And_DisplayName_MatchConvention()
    {
        IToolModule module = NewModule();

        Assert.Equal("context-menu", module.ModuleId);
        Assert.Equal("右键菜单管理", module.DisplayName);
    }

    [Fact]
    public async Task InitializeAsync_RootsReadable_Succeeds()
    {
        await NewModule().InitializeAsync();
    }

    [Fact]
    public async Task InitializeAsync_RootCannotBeOpened_ThrowsInvalidOperationWithAClearMessage()
    {
        var closed = Registry.CurrentUser.CreateSubKey(@"Software\PowerToolsTests\closed-" + Guid.NewGuid().ToString("N"));
        var path = closed.Name["HKEY_CURRENT_USER\\".Length..];
        closed.Close(); // using it now throws ObjectDisposedException
        try
        {
            var module = new ContextMenuModule(new ContextMenuRegistryRoots(closed, _sandbox.Roots.User), new PlainNames(), new CountingRestarter());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => module.InitializeAsync());

            Assert.Contains("右键菜单", ex.Message);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task LoadEntries_Disable_Reload_Enable_Reload_RoundTripsForAHandlerAndAVerb()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "WinRAR", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "WinRAR", RealDll);
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "git_shell", ("", "Git Bash Here"));
        var module = NewModule();
        await module.InitializeAsync();

        var entries = await module.LoadEntriesAsync();
        Assert.All(entries, e => Assert.Equal(MenuEntryState.Enabled, e.State));
        Assert.True(module.SetEntryEnabled(entries.Single(e => e.Kind == MenuEntryKind.Handler), false).Success);
        Assert.True(module.SetEntryEnabled(entries.Single(e => e.Kind == MenuEntryKind.Verb), false).Success);

        var disabled = await module.LoadEntriesAsync();
        Assert.All(disabled, e => Assert.Equal(MenuEntryState.Disabled, e.State));
        foreach (var entry in disabled) Assert.True(module.SetEntryEnabled(entry, true).Success);

        var enabledAgain = await module.LoadEntriesAsync();
        Assert.All(enabledAgain, e => Assert.Equal(MenuEntryState.Enabled, e.State));
    }

    [Fact]
    public async Task RestoreAll_TurnsEveryDisabledEntryBackOn_AndLeavesTheEnabledOnesAlone()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "WinRAR", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "WinRAR", RealDll);
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "git_shell", ("", "Git Bash Here"));
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "other", ("", "Other"));
        var module = NewModule();
        var entries = await module.LoadEntriesAsync();
        module.SetEntryEnabled(entries.Single(e => e.Kind == MenuEntryKind.Handler), false);
        module.SetEntryEnabled(entries.Single(e => e.KeyName == "git_shell"), false);

        var result = module.RestoreAll(await module.LoadEntriesAsync());

        Assert.Equal(2, result.Restored);
        Assert.Empty(result.Failures);
        Assert.True(result.NeedsExplorerRestart); // a handler came back; verbs alone would not need it
        Assert.All(await module.LoadEntriesAsync(), e => Assert.Equal(MenuEntryState.Enabled, e.State));
        using var untouched = _sandbox.Roots.User.OpenSubKey(@"Software\Classes\Directory\shell\other");
        Assert.Null(untouched);
    }

    [Fact]
    public async Task RestoreAll_AHandlerListedUnderSeveralLocations_IsRestoredAndCountedOnce()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Sharing", ClsidA);
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Directory, "Sharing", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Sharing", RealDll);
        var module = NewModule();
        module.SetEntryEnabled((await module.LoadEntriesAsync()).First(), false);

        var result = module.RestoreAll(await module.LoadEntriesAsync());

        Assert.Equal(1, result.Restored);
        Assert.All(await module.LoadEntriesAsync(), e => Assert.Equal(MenuEntryState.Enabled, e.State));
    }

    [Fact]
    public async Task RestoreAll_OnlyVerbs_DoesNotAskForAnExplorerRestart()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "git_shell", ("", "Git Bash Here"));
        var module = NewModule();
        module.SetEntryEnabled((await module.LoadEntriesAsync()).Single(), false);

        var result = module.RestoreAll(await module.LoadEntriesAsync());

        Assert.Equal(1, result.Restored);
        Assert.False(result.NeedsExplorerRestart);
    }

    [Fact]
    public async Task RestoreAll_AnEntryDisabledBySystem_IsNotOurs_AndIsNotTouched()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "vendor_tool", ("", "Vendor tool"), ("LegacyDisable", ""));
        var module = NewModule();
        var entries = await module.LoadEntriesAsync();
        Assert.Equal(MenuEntryState.DisabledBySystem, entries.Single().State);

        var result = module.RestoreAll(entries);

        Assert.Equal(0, result.Restored);
        Assert.Empty(result.Failures);
        using var key = _sandbox.Roots.User.OpenSubKey(@"Software\Classes\Directory\shell\vendor_tool");
        Assert.Null(key);
    }

    [Fact]
    public async Task RestoreAll_NothingDisabled_RestoresNothing()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "git_shell", ("", "Git Bash Here"));
        var module = NewModule();

        var result = module.RestoreAll(await module.LoadEntriesAsync());

        Assert.Equal(0, result.Restored);
        Assert.False(result.NeedsExplorerRestart);
    }

    [Fact]
    public async Task RestoreAll_WhenTheRegistryRefuses_EveryEntryIsListedAsFailed_NotJustTheFirst()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "git_shell", ("", "Git Bash Here"));
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "other", ("", "Other"));
        var module = NewModule();
        foreach (var entry in await module.LoadEntriesAsync()) module.SetEntryEnabled(entry, false);
        var disabled = await module.LoadEntriesAsync();
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var rule = new System.Security.AccessControl.RegistryAccessRule(me,
            System.Security.AccessControl.RegistryRights.SetValue, System.Security.AccessControl.AccessControlType.Deny);

        // The deny has to be on the verb keys themselves: a rule on their parent does not reach keys that already exist.
        var locked = new List<(RegistryKey Key, System.Security.AccessControl.RegistrySecurity Security)>();
        foreach (var verb in new[] { "git_shell", "other" })
        {
            var key = _sandbox.Hive(Hive.User).OpenSubKey($@"Software\Classes\Directory\shell\{verb}", writable: true)!;
            var security = key.GetAccessControl();
            security.AddAccessRule(rule);
            key.SetAccessControl(security);
            locked.Add((key, security));
        }

        try
        {
            var result = module.RestoreAll(disabled);

            Assert.Equal(0, result.Restored);
            Assert.Equal(2, result.Failures.Count);
        }
        finally
        {
            foreach (var (key, security) in locked)
            {
                security.RemoveAccessRule(rule);
                key.SetAccessControl(security);
                key.Dispose();
            }
        }
    }

    [Fact]
    public async Task SetClassicMenu_TogglesTheSwitch_AndIsClassicMenuEnabledFollows()
    {
        var module = NewModule();
        await module.InitializeAsync();
        Assert.False(module.IsClassicMenuEnabled);

        var on = module.SetClassicMenu(true);
        Assert.True(on.Success);
        Assert.True(on.NeedsExplorerRestart);
        Assert.True(module.IsClassicMenuEnabled);

        Assert.True(module.SetClassicMenu(false).Success);
        Assert.False(module.IsClassicMenuEnabled);
    }

    [Fact]
    public async Task SetAnalyzeMenu_AddsAndRemovesTheFolderVerbs_AndIsAnalyzeMenuEnabledFollows()
    {
        var module = new ContextMenuModule(_sandbox.Roots, new PlainNames(), new CountingRestarter(), @"C:\Apps\PowerTools.exe");
        await module.InitializeAsync();
        Assert.False(module.IsAnalyzeMenuEnabled);

        var on = module.SetAnalyzeMenu(true);
        Assert.True(on.Success);
        Assert.False(on.NeedsExplorerRestart); // static verbs show up at once
        Assert.True(module.IsAnalyzeMenuEnabled);
        Assert.Contains(await module.LoadEntriesAsync(), e => e.Id == "Directory|Verb|PowerTools.Analyze");

        Assert.True(module.SetAnalyzeMenu(false).Success);
        Assert.False(module.IsAnalyzeMenuEnabled);
    }

    [Fact]
    public async Task Starting_the_module_points_an_added_analyze_menu_at_the_current_icon()
    {
        const string oldIcon = @"C:\Apps\PowerTools_1.0.0.0\Assets\PowerTools.ico";
        const string newIcon = @"C:\Apps\PowerTools_1.0.1.0\Assets\PowerTools.ico";
        new ContextMenuModule(_sandbox.Roots, new PlainNames(), new CountingRestarter(), @"C:\Apps\PowerTools.exe", oldIcon).SetAnalyzeMenu(true);

        await new ContextMenuModule(_sandbox.Roots, new PlainNames(), new CountingRestarter(), @"C:\Apps\PowerTools.exe", newIcon).InitializeAsync();

        using var key = _sandbox.Roots.User.OpenSubKey(@"Software\Classes\Directory\shell\PowerTools.Analyze");
        Assert.Equal(newIcon, key!.GetValue("Icon"));
    }

    [Fact]
    public async Task RestartExplorerAsync_DelegatesToTheRestarter()
    {
        var restarter = new CountingRestarter();

        await NewModule(restarter).RestartExplorerAsync();

        Assert.Equal(1, restarter.Restarts);
    }
}
