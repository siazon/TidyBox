using System.Security;
using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;

namespace PowerTools.Modules.ContextMenu.Catalog;

/// <summary>Reads the context menu entries registered in the machine and user hives and merges them into one list.</summary>
public sealed class ContextMenuCatalog
{
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
        var entries = new List<MenuEntry>();

        foreach (var location in Enum.GetValues<MenuLocation>())
        {
            foreach (var keyName in KeyNames($@"{location.RegistryKeyName()}\shellex\ContextMenuHandlers"))
            {
                TryAdd(entries, () => BuildHandler(location, keyName, userBlocked, machineBlocked));
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

    private MenuEntry BuildHandler(MenuLocation location, string keyName, HashSet<string> userBlocked, HashSet<string> machineBlocked)
    {
        var relative = $@"{location.RegistryKeyName()}\shellex\ContextMenuHandlers\{keyName}";
        using var machineKey = OpenClasses(_roots.Machine, relative);
        using var userKey = OpenClasses(_roots.User, relative);

        // A handler key either holds the CLSID as its default value, or is itself named by the CLSID.
        var clsid = ClsidText.Normalize(FirstNonEmpty(Text(userKey), Text(machineKey))) ?? ClsidText.Normalize(keyName);

        string? registeredName = null;
        string? modulePath = null;
        var registered = false;
        if (clsid is not null)
        {
            foreach (var hive in new[] { _roots.User, _roots.Machine })
            {
                using var clsidKey = OpenClasses(hive, $@"CLSID\{clsid}");
                if (clsidKey is null) continue;

                registered = true;
                registeredName ??= FirstNonEmpty(Text(clsidKey));
                using var server = clsidKey.OpenSubKey("InprocServer32");
                modulePath ??= CleanPath(Text(server));
            }
        }

        var problem = clsid is null ? "无法识别处理程序"
            : !registered ? "未找到该处理程序的注册信息"
            : modulePath is not null && !File.Exists(modulePath) ? $"文件缺失：{modulePath}"
            : null;

        var state = clsid is not null && machineBlocked.Contains(clsid) ? MenuEntryState.DisabledBySystem
            : clsid is not null && userBlocked.Contains(clsid) ? MenuEntryState.Disabled
            : MenuEntryState.Enabled;

        var name = _names.Resolve(MenuEntryKind.Handler, keyName, clsid, modulePath, registeredName, null);
        return new MenuEntry($"{location}|{MenuEntryKind.Handler}|{keyName}", location, MenuEntryKind.Handler, keyName,
            name.DisplayName, name.Publisher, modulePath, clsid, machineKey is not null, userKey is not null, state, problem);
    }

    private MenuEntry BuildVerb(MenuLocation location, string keyName)
    {
        var relative = $@"{location.RegistryKeyName()}\shell\{keyName}";
        using var machineKey = OpenClasses(_roots.Machine, relative);
        using var userKey = OpenClasses(_roots.User, relative);

        var state = machineKey?.GetValue("LegacyDisable") is not null ? MenuEntryState.DisabledBySystem
            : userKey?.GetValue("LegacyDisable") is not null ? MenuEntryState.Disabled
            : MenuEntryState.Enabled;

        var muiVerb = FirstNonEmpty(Text(userKey, "MUIVerb"), Text(machineKey, "MUIVerb"));
        var registeredName = FirstNonEmpty(Text(userKey), Text(machineKey));

        var name = _names.Resolve(MenuEntryKind.Verb, keyName, null, null, registeredName, muiVerb);
        return new MenuEntry($"{location}|{MenuEntryKind.Verb}|{keyName}", location, MenuEntryKind.Verb, keyName,
            name.DisplayName, name.Publisher, null, null, machineKey is not null, userKey is not null, state, null);
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

    private static void TryAdd(List<MenuEntry> entries, Func<MenuEntry> build)
    {
        try
        {
            entries.Add(build());
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // Access denied or the key vanished while reading: skip this one entry, keep the rest.
        }
    }

    private static bool IsRegistryFailure(Exception ex) => ex is UnauthorizedAccessException or IOException or SecurityException;
}
