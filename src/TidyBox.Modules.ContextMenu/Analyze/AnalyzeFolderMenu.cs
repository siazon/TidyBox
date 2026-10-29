using System.Security;
using TidyBox.Modules.ContextMenu.Hives;
using TidyBox.Modules.ContextMenu.Toggle;

namespace TidyBox.Modules.ContextMenu.Analyze;

/// <summary>
/// The "analyze this folder" entries in Explorer's right-click menu: one for a folder you right-click, one for the empty
/// space inside an open folder. Static verbs in the current user's hive, added only when the user asks and removed again
/// on request; they take effect at once.
/// </summary>
public sealed class AnalyzeFolderMenu
{
    public const string VerbName = "TidyBox.Analyze";
    public const string Title = "分析此文件夹（TidyBox）";

    // Where each verb lives and what Explorer substitutes for the placeholder: %1 is the folder that was right-clicked,
    // %V the folder whose empty space was right-clicked.
    private static readonly (string Location, string Placeholder)[] Verbs =
    {
        ("Directory", "%1"),
        (@"Directory\Background", "%V"),
    };

    private readonly ContextMenuRegistryRoots _roots;
    private readonly string _executable;
    private readonly IRegistryWriter _writer;
    private readonly string? _icon;

    /// <param name="executable">The program the menu starts: a path that stays the same across app updates, such as an app execution alias.</param>
    /// <param name="icon">An .ico file shown next to the menu entries; none if null.</param>
    public AnalyzeFolderMenu(ContextMenuRegistryRoots roots, string executable, IRegistryWriter? writer = null, string? icon = null)
    {
        _roots = roots;
        _executable = executable;
        _writer = writer ?? new RegExeRegistryWriter();
        _icon = icon;
    }

    /// <summary>
    /// Points an already added menu at the current icon. The icon lives in the install folder, whose name has the version in
    /// it, so after an update the old path is dead. Does nothing when the menu is off or already right.
    /// </summary>
    /// <returns>True when something was rewritten.</returns>
    public bool RefreshIcon()
    {
        if (_icon is null || !IsEnabled) return false;

        try
        {
            var stale = Verbs.Where(verb => Named(VerbKey(verb.Location), "Icon") != _icon).ToList();
            foreach (var (location, _) in stale) _writer.SetString(_roots.User, VerbKey(location), "Icon", _icon);
            return stale.Count > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>True only when both verbs are there and start this program; a menu pointing elsewhere counts as off.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                return Verbs.All(verb => DefaultValue(CommandKey(verb.Location)) == Command(verb.Placeholder));
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
            foreach (var (location, placeholder) in Verbs)
            {
                if (enabled)
                {
                    _writer.SetString(_roots.User, VerbKey(location), null, Title);
                    if (_icon is not null) _writer.SetString(_roots.User, VerbKey(location), "Icon", _icon);
                    _writer.SetString(_roots.User, CommandKey(location), null, Command(placeholder));
                }
                else
                {
                    // Only our own verb; other verbs under the same shell key are not ours.
                    _writer.DeleteKey(_roots.User, VerbKey(location));
                }
            }

            return IsEnabled == enabled ? new ToggleResult(true, null) : new ToggleResult(false, "写入后状态没有改变。");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return new ToggleResult(false, ex.Message);
        }
    }

    // The "\." keeps a drive root (D:\) from ending in a backslash before the closing quote, which the command line
    // parser would read as an escaped quote.
    private string Command(string placeholder) => $"\"{_executable}\" --analyze \"{placeholder}\\.\"";

    private static string VerbKey(string location) => $@"{ContextMenuRegistryRoots.ClassesPath}\{location}\shell\{VerbName}";

    private static string CommandKey(string location) => VerbKey(location) + @"\command";

    private string? DefaultValue(string path) => Named(path, "");

    private string? Named(string path, string name)
    {
        using var key = _roots.User.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }
}
