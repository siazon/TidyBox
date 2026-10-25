using System.Security;
using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;

namespace PowerTools.Modules.ContextMenu.Catalog;

/// <summary>Reads the context menu entries registered in the machine and user hives and merges them into one list.</summary>
public sealed class ContextMenuCatalog
{
    private const string SystemDisabledReason = "系统已在机器级别禁用此项，无法在这里更改";
    private const string UnrecognisedReason = "无法识别此处理程序，不能更改";
    private const string CoreVerbReason = "这是资源管理器的核心命令，禁用后可能影响打开文件夹，因此不允许在这里更改";

    // Other things an extension can be besides a context menu handler. Blocking a CLSID switches off all of them.
    private static readonly (string HandlerType, string Role)[] OtherRoles =
    {
        ("PropertySheetHandlers", "属性页"),
        ("DragDropHandlers", "拖放菜单"),
        ("CopyHookHandlers", "文件复制钩子"),
    };

    private static readonly HashSet<string> CoreVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "explore", "opennewwindow", "opennewprocess", "opennewtab",
    };

    private static readonly HashSet<MenuLocation> CoreVerbLocations = new() { MenuLocation.Folder, MenuLocation.Directory, MenuLocation.Drive };

    private readonly ContextMenuRegistryRoots _roots;
    private readonly IMenuNameResolver _names;

    public ContextMenuCatalog(ContextMenuRegistryRoots roots, IMenuNameResolver names)
    {
        _roots = roots;
        _names = names;
    }

    public IReadOnlyList<MenuEntry> LoadAll()
    {
        var userBlocked = BlockedClsids(_roots.User);
        var machineBlocked = BlockedClsids(_roots.Machine);
        var otherRoles = CollectOtherRoles();
        var entries = new List<MenuEntry>();

        foreach (var location in Enum.GetValues<MenuLocation>())
        {
            foreach (var keyName in KeyNames($@"{location.RegistryKeyName()}\shellex\ContextMenuHandlers"))
            {
                TryAdd(entries, () => BuildHandler(location, keyName, userBlocked, machineBlocked, otherRoles));
            }

            foreach (var keyName in KeyNames($@"{location.RegistryKeyName()}\shell"))
            {
                TryAdd(entries, () => BuildVerb(location, keyName));
            }
        }

        return entries
            .OrderBy(e => e.Location)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    private MenuEntry BuildHandler(
        MenuLocation location, string keyName, HashSet<string> userBlocked, HashSet<string> machineBlocked, Dictionary<string, SortedSet<string>> otherRoles)
    {
        var relative = $@"{location.RegistryKeyName()}\shellex\ContextMenuHandlers\{keyName}";
        using var machineKey = OpenClasses(_roots.Machine, relative);
        using var userKey = OpenClasses(_roots.User, relative);

        // A handler key either holds the CLSID as its default value, or is itself named by the CLSID.
        var clsid = ClsidText.Normalize(FirstNonEmpty(Text(userKey), Text(machineKey))) ?? ClsidText.Normalize(keyName);

        string? registeredName = null;
        string? rawModulePath = null;
        var registered = false;
        var isNamespaceExtension = false;
        if (clsid is not null)
        {
            foreach (var hive in new[] { _roots.User, _roots.Machine })
            {
                using var clsidKey = OpenClasses(hive, $@"CLSID\{clsid}");
                if (clsidKey is null) continue;

                registered = true;
                registeredName ??= FirstNonEmpty(Text(clsidKey));
                using var server = clsidKey.OpenSubKey("InprocServer32");
                rawModulePath ??= CleanPath(Text(server));
                using var shellFolder = clsidKey.OpenSubKey("ShellFolder");
                isNamespaceExtension |= shellFolder is not null;
            }
        }

        var (modulePath, canCheckFile) = ResolveModule(rawModulePath);

        var problem = clsid is null ? "无法识别处理程序"
            : !registered ? "未找到该处理程序的注册信息"
            : modulePath is not null && canCheckFile && !File.Exists(modulePath) ? $"文件缺失：{modulePath}"
            : null;

        var state = clsid is not null && machineBlocked.Contains(clsid) ? MenuEntryState.DisabledBySystem
            : clsid is not null && userBlocked.Contains(clsid) ? MenuEntryState.Disabled
            : MenuEntryState.Enabled;

        var lockedReason = state == MenuEntryState.DisabledBySystem ? SystemDisabledReason
            : clsid is null ? UnrecognisedReason
            : null;

        var roles = new List<string>();
        if (clsid is not null && otherRoles.TryGetValue(clsid, out var found)) roles.AddRange(found);
        if (isNamespaceExtension) roles.Add("命名空间扩展");
        var warning = roles.Count == 0 ? null : $"此扩展还用作{string.Join("、", roles)}，禁用会一并停用这些功能";

        var name = _names.Resolve(MenuEntryKind.Handler, keyName, clsid, modulePath, registeredName, null);
        return new MenuEntry($"{location}|{MenuEntryKind.Handler}|{keyName}", location, MenuEntryKind.Handler, keyName,
            name.DisplayName, name.Publisher, modulePath, clsid, machineKey is not null, userKey is not null, state, problem, warning, lockedReason);
    }

    private MenuEntry? BuildVerb(MenuLocation location, string keyName)
    {
        var relative = $@"{location.RegistryKeyName()}\shell\{keyName}";
        using var machineKey = OpenClasses(_roots.Machine, relative);
        using var userKey = OpenClasses(_roots.User, relative);

        // Such verbs exist only for programs to invoke; they never appear in any menu.
        if (machineKey?.GetValue("ProgrammaticAccessOnly") is not null || userKey?.GetValue("ProgrammaticAccessOnly") is not null) return null;

        var state = machineKey?.GetValue("LegacyDisable") is not null ? MenuEntryState.DisabledBySystem
            : userKey?.GetValue("LegacyDisable") is not null ? MenuEntryState.Disabled
            : MenuEntryState.Enabled;

        var lockedReason = state == MenuEntryState.DisabledBySystem ? SystemDisabledReason
            : CoreVerbLocations.Contains(location) && CoreVerbs.Contains(keyName.Trim()) ? CoreVerbReason
            : null;

        var shiftOnly = machineKey?.GetValue("Extended") is not null || userKey?.GetValue("Extended") is not null;
        var warning = shiftOnly ? "仅在按住 Shift 再右键时才会显示" : null;

        var muiVerb = FirstNonEmpty(Text(userKey, "MUIVerb"), Text(machineKey, "MUIVerb"));
        var registeredName = FirstNonEmpty(Text(userKey), Text(machineKey));

        var name = _names.Resolve(MenuEntryKind.Verb, keyName, null, null, registeredName, muiVerb);
        return new MenuEntry($"{location}|{MenuEntryKind.Verb}|{keyName}", location, MenuEntryKind.Verb, keyName,
            name.DisplayName, name.Publisher, null, null, machineKey is not null, userKey is not null, state, null, warning, lockedReason);
    }

    // CLSID -> the other roles (property page, drag-and-drop, copy hook) it is registered for, in any location.
    private Dictionary<string, SortedSet<string>> CollectOtherRoles()
    {
        var roles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (handlerType, role) in OtherRoles)
        {
            foreach (var location in Enum.GetValues<MenuLocation>())
            {
                var folder = $@"{location.RegistryKeyName()}\shellex\{handlerType}";
                foreach (var keyName in KeyNames(folder))
                {
                    try
                    {
                        using var machineKey = OpenClasses(_roots.Machine, $@"{folder}\{keyName}");
                        using var userKey = OpenClasses(_roots.User, $@"{folder}\{keyName}");
                        var clsid = ClsidText.Normalize(FirstNonEmpty(Text(userKey), Text(machineKey))) ?? ClsidText.Normalize(keyName);
                        if (clsid is null) continue;

                        if (!roles.TryGetValue(clsid, out var set)) roles[clsid] = set = new SortedSet<string>(StringComparer.Ordinal);
                        set.Add(role);
                    }
                    catch (Exception ex) when (IsRegistryFailure(ex))
                    {
                        // An unreadable key only means one fewer warning.
                    }
                }
            }
        }

        return roles;
    }

    // Key names present in either hive. Registry names are case-insensitive; the machine hive's spelling wins.
    private IEnumerable<string> KeyNames(string relative)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { _roots.Machine, _roots.User })
        {
            try
            {
                using var key = OpenClasses(hive, relative);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames()) names.TryAdd(name, name);
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                // One unreadable hive must not hide what the other one holds.
            }
        }

        return names.Values;
    }

    private static HashSet<string> BlockedClsids(RegistryKey hive)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = hive.OpenSubKey(ContextMenuRegistryRoots.BlockedPath);
            if (key is null) return result;
            foreach (var name in key.GetValueNames())
            {
                var clsid = ClsidText.Normalize(name);
                if (clsid is not null) result.Add(clsid);
            }
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // Treat an unreadable blocked list as empty.
        }

        return result;
    }

    private static RegistryKey? OpenClasses(RegistryKey hive, string relative)
        => hive.OpenSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\{relative}");

    private static string? Text(RegistryKey? key, string name = "") => key?.GetValue(name) as string;

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    // InprocServer32 sometimes holds a quoted path.
    private static string? CleanPath(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().Trim('"');

    // Registrations may use %SystemRoot%-style variables (REG_SZ is not expanded by the registry API) or a bare file
    // name that Windows finds in System32. A bare name we cannot find is not reported: we do not know where Windows looks.
    private static (string? Path, bool CanCheck) ResolveModule(string? raw)
    {
        if (raw is null) return (null, false);

        var expanded = Environment.ExpandEnvironmentVariables(raw);
        if (Path.IsPathRooted(expanded)) return (expanded, true);

        var inSystem = Path.Combine(Environment.SystemDirectory, expanded);
        return File.Exists(inSystem) ? (inSystem, true) : (expanded, false);
    }

    private static void TryAdd(List<MenuEntry> entries, Func<MenuEntry?> build)
    {
        try
        {
            var entry = build();
            if (entry is not null) entries.Add(entry);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // Access denied or the key vanished while reading: skip this one entry, keep the rest.
        }
    }

    private static bool IsRegistryFailure(Exception ex) => ex is UnauthorizedAccessException or IOException or SecurityException;
}
