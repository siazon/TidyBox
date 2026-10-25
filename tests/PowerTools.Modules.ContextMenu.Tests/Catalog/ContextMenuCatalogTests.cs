using PowerTools.Modules.ContextMenu.Catalog;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;
using PowerTools.Modules.ContextMenu.Tests.Support;

namespace PowerTools.Modules.ContextMenu.Tests.Catalog;

public class ContextMenuCatalogTests : IDisposable
{
    private const string ClsidA = "{AAAAAAAA-0000-0000-0000-00000000000A}";
    private const string ClsidB = "{BBBBBBBB-0000-0000-0000-00000000000B}";
    private static readonly string RealDll = Path.Combine(Environment.SystemDirectory, "shell32.dll");

    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private sealed class RecordingResolver : IMenuNameResolver
    {
        public List<(MenuEntryKind Kind, string KeyName, string? Clsid, string? ModulePath, string? RegisteredName, string? MuiVerb)> Calls { get; } = new();
        public Func<string, bool> ThrowFor { get; set; } = _ => false;

        public MenuNameInfo Resolve(MenuEntryKind kind, string keyName, string? clsid, string? modulePath, string? registeredName, string? muiVerb)
        {
            if (ThrowFor(keyName)) throw new UnauthorizedAccessException("simulated denied key");
            Calls.Add((kind, keyName, clsid, modulePath, registeredName, muiVerb));
            return new MenuNameInfo(keyName.Trim(), "Fake Publisher");
        }
    }

    private (ContextMenuCatalog Catalog, RecordingResolver Resolver) Catalog()
    {
        var resolver = new RecordingResolver();
        return (new ContextMenuCatalog(_sandbox.Roots, resolver), resolver);
    }

    private IReadOnlyList<MenuEntry> Load() => Catalog().Catalog.LoadAll();

    [Fact]
    public void LoadAll_HandlerWithClsidAsDefaultValue_ReturnsEntryWithThatClsid()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "WinRAR", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "WinRAR", RealDll);

        var entry = Load().Single();

        Assert.Equal(ClsidA, entry.Clsid);
        Assert.Equal(MenuEntryKind.Handler, entry.Kind);
        Assert.Equal(MenuLocation.AllFiles, entry.Location);
        Assert.Equal(RealDll, entry.ModulePath);
        Assert.Null(entry.Problem);
        Assert.Equal(MenuEntryState.Enabled, entry.State);
        Assert.Equal("Fake Publisher", entry.Publisher);
    }

    [Fact]
    public void LoadAll_HandlerWhoseKeyNameIsTheClsid_UsesTheKeyName()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, ClsidA, "Taskband Pin");
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Taskband Pin", RealDll);

        var entry = Load().Single();

        Assert.Equal(ClsidA, entry.Clsid);
        Assert.Null(entry.Problem);
    }

    [Fact]
    public void LoadAll_KeyNameWithLeadingSpace_KeepsItInKeyNameAndId()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, " FileSyncEx", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "FileSyncEx", RealDll);

        var entry = Load().Single();

        Assert.Equal(" FileSyncEx", entry.KeyName);
        Assert.Equal("AllFiles|Handler| FileSyncEx", entry.Id);
    }

    [Fact]
    public void LoadAll_SameHandlerInMachineAndUserHive_IsOneEntryMarkedInBoth()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Directory, "Sharing", ClsidA);
        _sandbox.AddHandler(Hive.User, MenuLocation.Directory, "Sharing", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Sharing", RealDll);

        var entry = Load().Single();

        Assert.True(entry.InMachineHive);
        Assert.True(entry.InUserHive);
    }

    [Fact]
    public void LoadAll_HandlerOnlyInUserHive_IsMarkedUserOnly()
    {
        _sandbox.AddHandler(Hive.User, MenuLocation.DirectoryBackground, "PowerRenameExt", ClsidA);
        _sandbox.RegisterClsid(Hive.User, ClsidA, "PowerRename", RealDll);

        var entry = Load().Single();

        Assert.False(entry.InMachineHive);
        Assert.True(entry.InUserHive);
    }

    [Fact]
    public void LoadAll_HandlerClsidBlockedInUserList_IsDisabled()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "WinRAR", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "WinRAR", RealDll);
        _sandbox.Block(Hive.User, ClsidA.ToLowerInvariant()); // blocked-list names are matched case-insensitively

        Assert.Equal(MenuEntryState.Disabled, Load().Single().State);
    }

    [Fact]
    public void LoadAll_HandlerClsidBlockedOnlyInMachineList_IsDisabledBySystem()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "WinRAR", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "WinRAR", RealDll);
        _sandbox.Block(Hive.Machine, ClsidA);

        Assert.Equal(MenuEntryState.DisabledBySystem, Load().Single().State);
    }

    [Fact]
    public void LoadAll_VerbWithLegacyDisableInUserHive_IsDisabled()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "git_shell", ("", "Git Bash Here"));
        _sandbox.AddVerb(Hive.User, MenuLocation.Directory, "git_shell", ("LegacyDisable", ""));

        var entry = Load().Single();

        Assert.Equal(MenuEntryKind.Verb, entry.Kind);
        Assert.Equal(MenuEntryState.Disabled, entry.State);
        Assert.True(entry.InMachineHive);
        Assert.True(entry.InUserHive);
    }

    [Fact]
    public void LoadAll_VerbWithLegacyDisableOnlyInMachineHive_IsDisabledBySystem()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "find", ("LegacyDisable", ""));

        Assert.Equal(MenuEntryState.DisabledBySystem, Load().Single().State);
    }

    [Fact]
    public void LoadAll_VerbPlainEntry_IsEnabledAndPassesItsNamesToTheResolver()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "AnyCode", ("", "Open with Code"), ("MUIVerb", "@shell32.dll,-1"));
        var (catalog, resolver) = Catalog();

        var entry = catalog.LoadAll().Single();

        Assert.Equal(MenuEntryState.Enabled, entry.State);
        var call = resolver.Calls.Single();
        Assert.Equal(MenuEntryKind.Verb, call.Kind);
        Assert.Equal("Open with Code", call.RegisteredName);
        Assert.Equal("@shell32.dll,-1", call.MuiVerb);
    }

    [Fact]
    public void LoadAll_UnregisteredClsid_IsListedWithAProblem()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Folder, "WinRAR32", ClsidB);

        var entry = Load().Single();

        Assert.Equal(ClsidB, entry.Clsid);
        Assert.Contains("未找到", entry.Problem);
    }

    [Fact]
    public void LoadAll_DllMissing_IsListedWithAProblemNamingTheFile()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Gone", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Gone", @"C:\definitely\not\here.dll");

        var entry = Load().Single();

        Assert.Contains(@"C:\definitely\not\here.dll", entry.Problem);
        Assert.Contains("文件缺失", entry.Problem);
    }

    [Fact]
    public void LoadAll_NoClsidAnywhere_IsListedWithAProblem()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Mystery", "not a clsid");

        var entry = Load().Single();

        Assert.Null(entry.Clsid);
        Assert.Contains("无法识别", entry.Problem);
    }

    [Fact]
    public void LoadAll_OneEntryFailsWhileBeingRead_OtherEntriesStillLoad()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Bad", ClsidA);
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Good", ClsidB);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Bad", RealDll);
        _sandbox.RegisterClsid(Hive.Machine, ClsidB, "Good", RealDll);
        var (catalog, resolver) = Catalog();
        resolver.ThrowFor = key => key == "Bad";

        var entries = catalog.LoadAll();

        Assert.Equal("Good", entries.Single().KeyName);
    }

    [Fact]
    public void LoadAll_IsOrderedByLocationThenKindThenName()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "aaa-verb");
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Directory, "zzz-handler", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "zzz", RealDll);
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "mmm-handler", ClsidB);
        _sandbox.RegisterClsid(Hive.Machine, ClsidB, "mmm", RealDll);

        var ids = Load().Select(e => e.Id).ToArray();

        Assert.Equal(new[] { "AllFiles|Handler|mmm-handler", "Directory|Handler|zzz-handler", "Directory|Verb|aaa-verb" }, ids);
    }

    [Fact]
    public void LoadAll_RealRegistry_ReadsWithoutThrowing_AndEveryEntryHasAUniqueIdAndAName()
    {
        // Read-only smoke test against the machine this runs on: real data has shapes no sandbox thinks of.
        var entries = new ContextMenuCatalog(ContextMenuRegistryRoots.Real(), MenuNameResolver.CreateDefault()).LoadAll();

        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.DisplayName), $"{e.Id} has no display name"));
    }

    [Fact]
    public void LoadAll_NothingRegistered_ReturnsEmpty()
    {
        Assert.Empty(Load());
    }
}
