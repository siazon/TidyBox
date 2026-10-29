using System.Text.RegularExpressions;

namespace TidyBox.Modules.Uninstaller.Routing;

public record UninstallCommand(string FileName, string Arguments);

/// <summary>Splits the UninstallString of a registry entry into the program to run and its arguments.</summary>
public static class UninstallCommandParser
{
    private static readonly Regex MsiInstallSwitch = new(@"/I\s*(?=\{)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <returns>The command, or null when the text names no program.</returns>
    public static UninstallCommand? Parse(string? uninstallString)
    {
        var text = Environment.ExpandEnvironmentVariables(uninstallString ?? "").Trim();
        if (text.Length == 0) return null;

        string fileName, arguments;
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close < 0) return null;
            fileName = text.Substring(1, close - 1);
            arguments = text.Substring(close + 1).Trim();
        }
        else
        {
            (fileName, arguments) = SplitUnquoted(text);
        }

        if (fileName.Length == 0) return null;

        // Windows Installer lists "MsiExec /I{product}" (which opens the repair dialog); /X is the actual uninstall.
        if (Path.GetFileName(fileName).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(fileName).Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            arguments = MsiInstallSwitch.Replace(arguments, "/X");
        }

        return new UninstallCommand(fileName, arguments);
    }

    // An unquoted path may contain spaces; the longest-first guess is whichever prefix is a real file, else the first word.
    private static (string FileName, string Arguments) SplitUnquoted(string text)
    {
        for (var space = text.IndexOf(' '); space >= 0; space = text.IndexOf(' ', space + 1))
        {
            var candidate = text.Substring(0, space);
            if (File.Exists(candidate)) return (candidate, text.Substring(space + 1).Trim());
        }

        if (File.Exists(text)) return (text, "");

        var first = text.IndexOf(' ');
        return first < 0 ? (text, "") : (text.Substring(0, first), text.Substring(first + 1).Trim());
    }
}
