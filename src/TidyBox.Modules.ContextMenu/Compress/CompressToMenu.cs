using System.Security;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Toggle;

namespace TidyBox.Modules.ContextMenu.Compress;

/// <summary>
/// A "压缩到" submenu for the classic right-click menu, which Windows 11 only gives to the new one. It is one static verb in
/// the current user's hive that lists the system's own compress commands (the ones the new menu uses), so Windows does the
/// archiving, including one archive per selected item, and TidyBox contains no compression code. Added only when the user asks
/// and removed again on request; it takes effect at once.
/// </summary>
public sealed class CompressToMenu
{
    public const string VerbName = "TidyBox.CompressTo";
    public const string Title = "压缩到";

    private const string VerbPath = $@"{ContextMenuRegistryRoots.ClassesPath}\AllFilesystemObjects\shell\{VerbName}";
    private const string CommandStore = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";

    // The order the new menu shows them in. A machine that lacks one (an older Windows 11 build) simply gets a shorter list.
    private static readonly string[] CommandNames =
    {
        "Windows.CompressTo.Zip", "Windows.CompressTo.7Z", "Windows.CompressTo.Tar", "Windows.CompressTo.Wizard",
    };

    private readonly ContextMenuRegistryRoots _roots;
    private readonly IRegistryWriter _writer;

    public CompressToMenu(ContextMenuRegistryRoots roots, IRegistryWriter? writer = null)
    {
        _roots = roots;
        _writer = writer ?? new RegExeRegistryWriter();
    }

    /// <summary>False when this Windows has none of the commands, so there is nothing to put in the submenu.</summary>
    public bool IsAvailable => SystemCommands().Count > 0;

    /// <summary>True only when the verb is there and lists exactly what this system offers; a stale list counts as off.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                var commands = SystemCommands();
                return commands.Count > 0 && Named("MUIVerb") == Title && Named("SubCommands") == string.Join(';', commands);
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
            if (enabled)
            {
                var commands = SystemCommands();
                if (commands.Count == 0) return new ToggleResult(false, "这个版本的 Windows 没有自带的“压缩到”命令。");

                _writer.SetString(_roots.User, VerbPath, "MUIVerb", Title);
                _writer.SetString(_roots.User, VerbPath, "SubCommands", string.Join(';', commands));
            }
            else
            {
                // Only our own verb; other verbs under the same shell key are not ours.
                _writer.DeleteKey(_roots.User, VerbPath);
            }

            return IsEnabled == enabled ? new ToggleResult(true, null) : new ToggleResult(false, "写入后状态没有改变。");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return new ToggleResult(false, ex.Message);
        }
    }

    private List<string> SystemCommands()
    {
        try
        {
            return CommandNames.Where(name =>
            {
                using var key = _roots.Machine.OpenSubKey($@"{CommandStore}\{name}");
                return key is not null;
            }).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return new List<string>();
        }
    }

    private string? Named(string name)
    {
        using var key = _roots.User.OpenSubKey(VerbPath);
        return key?.GetValue(name) as string;
    }
}
