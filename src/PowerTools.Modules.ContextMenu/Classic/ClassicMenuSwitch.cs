using System.Security;
using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Toggle;

namespace PowerTools.Modules.ContextMenu.Classic;

/// <summary>
/// Windows 11's "show the full classic context menu directly" switch: an empty InprocServer32 for a well-known
/// CLSID in the current user's hive. Explorer only reads it at start, so every change needs an Explorer restart.
/// </summary>
public sealed class ClassicMenuSwitch
{
    private const string SwitchKeyPath = $@"{ContextMenuRegistryRoots.ClassesPath}\CLSID\{{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}}";

    private readonly ContextMenuRegistryRoots _roots;

    public ClassicMenuSwitch(ContextMenuRegistryRoots roots)
    {
        _roots = roots;
    }

    /// <summary>True only for the exact shape Explorer honours: InprocServer32 whose default value is an empty string.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = _roots.User.OpenSubKey(SwitchKeyPath + @"\InprocServer32");
                return key?.GetValue("") is "";
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                return false;
            }
        }
    }

    public ToggleResult SetEnabled(bool enabled)
    {
        try
        {
            var wasEnabled = IsEnabled;
            if (enabled)
            {
                if (!wasEnabled)
                {
                    using var key = _roots.User.CreateSubKey(SwitchKeyPath + @"\InprocServer32");
                    key.SetValue("", "", RegistryValueKind.String);
                }
            }
            else
            {
                // Only this CLSID's key; other CLSID keys in the user hive are not ours.
                _roots.User.DeleteSubKeyTree(SwitchKeyPath, throwOnMissingSubKey: false);
            }

            if (IsEnabled != enabled) return new ToggleResult(false, "写入后状态没有改变。");
            return new ToggleResult(true, null, NeedsExplorerRestart: enabled != wasEnabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return new ToggleResult(false, ex.Message);
        }
    }
}
