using TidyBox.Modules.ContextMenu.Catalog;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Model;
using TidyBox.Modules.ContextMenu.Naming;
using TidyBox.Modules.ContextMenu.Tests.Support;

namespace TidyBox.Modules.ContextMenu.Tests.Catalog;

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

    // ---- F2: what may not be changed here, and what is not really a menu entry ----

    [Fact]
    public void LoadAll_EntryDisabledBySystem_IsLockedWithAReasonTheViewCanShow()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "find", ("LegacyDisable", ""));

        var entry = Load().Single();

        Assert.Contains("系统", entry.LockedReason);
    }

    [Fact]
    public void LoadAll_UnrecognisedHandler_IsLocked()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Mystery", "not a clsid");

        Assert.Contains("无法识别", Load().Single().LockedReason);
    }

    [Theory]
    [InlineData(MenuLocation.Folder, "open")]
    [InlineData(MenuLocation.Directory, "explore")]
    [InlineData(MenuLocation.Drive, "opennewwindow")]
    [InlineData(MenuLocation.Folder, "OpenNewProcess")]
    [InlineData(MenuLocation.Folder, "opennewtab")]
    public void LoadAll_CoreExplorerVerbs_AreLocked(MenuLocation location, string verb)
    {
        _sandbox.AddVerb(Hive.Machine, location, verb, ("", verb));

        Assert.Contains("核心", Load().Single().LockedReason);
    }

    [Fact]
    public void LoadAll_OrdinaryVerbAndHandler_AreNotLockedAndHaveNoWarning()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "cmd", ("", "cmd"));
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "WinRAR", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "WinRAR", RealDll);

        Assert.All(Load(), e =>
        {
            Assert.Null(e.LockedReason);
            Assert.Null(e.Warning);
        });
    }

    [Fact]
    public void LoadAll_VerbThatIsProgrammaticAccessOnly_IsNotListedBecauseItNeverAppearsInAMenu()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.AllFiles, "removeproperties", ("ProgrammaticAccessOnly", ""));
        _sandbox.AddVerb(Hive.Machine, MenuLocation.AllFiles, "visible", ("", "visible"));

        Assert.Equal("visible", Load().Single().KeyName);
    }

    [Fact]
    public void LoadAll_ExtendedVerb_IsMarkedAsShownOnlyWithShift()
    {
        _sandbox.AddVerb(Hive.Machine, MenuLocation.Directory, "cmd", ("Extended", ""));

        Assert.Contains("Shift", Load().Single().Warning);
    }

    // ---- F1: Blocked is per CLSID, so disabling a menu entry can switch off the same extension elsewhere ----

    [Theory]
    [InlineData("PropertySheetHandlers", "属性页")]
    [InlineData("DragDropHandlers", "拖放")]
    [InlineData("CopyHookHandlers", "复制")]
    public void LoadAll_HandlerThatIsAlsoRegisteredForAnotherRole_WarnsThatDisablingStopsThatToo(string handlerType, string roleWord)
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Directory, "Sharing", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Sharing", RealDll);
        _sandbox.AddShellexHandler(Hive.Machine, MenuLocation.Drive, handlerType, "Sharing", ClsidA);

        var entry = Load().Single(e => e.Kind == MenuEntryKind.Handler);

        Assert.Contains(roleWord, entry.Warning);
        Assert.Null(entry.LockedReason); // still the user's call
    }

    [Fact]
    public void LoadAll_HandlerThatIsAlsoANamespaceExtension_WarnsAboutIt()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.Drive, "{fbeb8a05-beee-4442-804e-409d6c4515e9}", "");
        _sandbox.RegisterClsid(Hive.Machine, "{FBEB8A05-BEEE-4442-804E-409D6C4515E9}", "ShellFolder for CD Burning", RealDll);
        _sandbox.AddClsidSubKey(Hive.Machine, "{FBEB8A05-BEEE-4442-804E-409D6C4515E9}", "ShellFolder");

        Assert.Contains("命名空间", Load().Single().Warning);
    }

    [Fact]
    public void LoadAll_PropertySheetHandlerAlone_IsNotListedAsAMenuEntry()
    {
        _sandbox.AddShellexHandler(Hive.Machine, MenuLocation.Directory, "PropertySheetHandlers", "OnlyAPage", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Page", RealDll);

        Assert.Empty(Load());
    }

    // ---- F6: InprocServer32 paths as they really occur ----

    [Fact]
    public void LoadAll_ModulePathWithEnvironmentVariable_IsExpandedAndNotReportedMissing()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Env", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Env", @"%SystemRoot%\System32\shell32.dll");

        var entry = Load().Single();

        Assert.Equal(RealDll, entry.ModulePath, ignoreCase: true);
        Assert.Null(entry.Problem);
    }

    [Fact]
    public void LoadAll_BareDllNameInSystem32_IsResolvedAndNotReportedMissing()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Bare", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Bare", "shell32.dll");

        var entry = Load().Single();

        Assert.Equal(RealDll, entry.ModulePath, ignoreCase: true);
        Assert.Null(entry.Problem);
    }

    [Fact]
    public void LoadAll_BareDllNameNotInSystem32_IsNotAccusedOfBeingMissing()
    {
        _sandbox.AddHandler(Hive.Machine, MenuLocation.AllFiles, "Elsewhere", ClsidA);
        _sandbox.RegisterClsid(Hive.Machine, ClsidA, "Elsewhere", "somewhere-on-the-path-xyz.dll");

        Assert.Null(Load().Single().Problem); // we cannot tell where Windows would look for it
    }

    [Fact]
    public void LoadAll_NothingRegistered_ReturnsEmpty()
    {
        Assert.Empty(Load());
    }
}
