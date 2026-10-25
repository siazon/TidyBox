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
    public async Task RestartExplorerAsync_DelegatesToTheRestarter()
    {
        var restarter = new CountingRestarter();

        await NewModule(restarter).RestartExplorerAsync();

        Assert.Equal(1, restarter.Restarts);
    }
}
