using System.Security;
using Microsoft.Win32;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Model;

namespace TidyBox.Modules.ContextMenu.Toggle;

/// <param name="NeedsExplorerRestart">True when something actually changed and Explorer only picks it up after a restart.</param>
public record ToggleResult(bool Success, string? Error, bool NeedsExplorerRestart = false);

/// <summary>
/// Enables or disables an entry for the current user, reversibly. It only ever adds or removes values it owns
/// (a CLSID in the user's Blocked list, or LegacyDisable on a verb) in the user hive; handler keys, CLSID keys and
/// the whole machine hive are never modified.
/// </summary>
public sealed class MenuToggler
{
    private const string LegacyDisable = "LegacyDisable";

    private readonly ContextMenuRegistryRoots _roots;
    private readonly IRegistryWriter _writer;

    public MenuToggler(ContextMenuRegistryRoots roots, IRegistryWriter? writer = null)
    {
        _roots = roots;
        _writer = writer ?? new RegExeRegistryWriter();
    }

    public ToggleResult SetEnabled(MenuEntry entry, bool enabled)
    {
        if (entry.LockedReason is not null)
        {
            return Fail(entry.LockedReason);
        }

        if (entry.State == MenuEntryState.DisabledBySystem)
        {
            return Fail("该项目在系统级已被禁用，无法在这里更改。");
        }

        if (entry.Kind == MenuEntryKind.Handler && entry.Clsid is null)
        {
            return Fail(entry.Problem ?? "无法识别处理程序");
        }

        try
        {
            return entry.Kind == MenuEntryKind.Handler
                ? SetHandlerEnabled(entry.Clsid!, enabled)
                : SetVerbEnabled(entry, enabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return Fail(ex.Message);
        }
    }

    // Handlers: the CLSID goes on the per-user Blocked list. It applies to that CLSID everywhere it is registered.
    private ToggleResult SetHandlerEnabled(string clsid, bool enabled)
    {
        var changed = false;
        using (var key = _roots.User.OpenSubKey(ContextMenuRegistryRoots.BlockedPath))
        {
            var names = key?.GetValueNames() ?? Array.Empty<string>();
            if (enabled)
            {
                foreach (var name in names.Where(name => ClsidText.Normalize(name) == clsid))
                {
                    _writer.DeleteValue(_roots.User, ContextMenuRegistryRoots.BlockedPath, name);
                    changed = true;
                }
            }
            else if (!names.Any(name => ClsidText.Normalize(name) == clsid))
            {
                _writer.SetString(_roots.User, ContextMenuRegistryRoots.BlockedPath, clsid, "");
                changed = true;
            }
        }

        return IsBlockedForUser(clsid) == !enabled
            ? new ToggleResult(true, null, NeedsExplorerRestart: changed)
            : Fail("写入后状态没有改变。");
    }

    // Verbs: LegacyDisable in a user-hive override key. The machine hive's definition stays as it is; the merged
    // registry view takes the user's value on top of it.
    private ToggleResult SetVerbEnabled(MenuEntry entry, bool enabled)
    {
        var relative = $@"{ContextMenuRegistryRoots.ClassesPath}\{entry.Location.RegistryKeyName()}\shell\{entry.KeyName}";

        if (enabled)
        {
            _writer.DeleteValue(_roots.User, relative, LegacyDisable);

            // A key with nothing left in it is no verb anyone can use: it can only be our own leftover override.
            bool empty;
            using (var remaining = _roots.User.OpenSubKey(relative)) empty = remaining is { ValueCount: 0, SubKeyCount: 0 };
            if (empty) _writer.DeleteKey(_roots.User, relative);
        }
        else
        {
            bool alreadyDisabled;
            using (var key = _roots.User.OpenSubKey(relative)) alreadyDisabled = key?.GetValue(LegacyDisable) is not null;
            if (!alreadyDisabled) _writer.SetString(_roots.User, relative, LegacyDisable, "");
        }

        using var check = _roots.User.OpenSubKey(relative);
        return (check?.GetValue(LegacyDisable) is not null) == !enabled
            ? new ToggleResult(true, null)
            : Fail("写入后状态没有改变。");
    }

    private bool IsBlockedForUser(string clsid)
    {
        using var key = _roots.User.OpenSubKey(ContextMenuRegistryRoots.BlockedPath);
        return key is not null && key.GetValueNames().Any(name => ClsidText.Normalize(name) == clsid);
    }

    private static ToggleResult Fail(string error) => new(false, error);
}
