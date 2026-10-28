namespace PowerTools.Core.Logging;

/// <summary>Removes who and where the user is from log text before it leaves the machine.</summary>
public static class LogRedactor
{
    // A shorter name would also match inside ordinary words ("Al" in "Algiers"), mangling the text for no gain.
    private const int MinNameLength = 3;

    /// <summary>
    /// The profile folder becomes <c>%USERPROFILE%</c>, the user name <c>&lt;user&gt;</c> and the machine name
    /// <c>&lt;machine&gt;</c>, ignoring case. The profile goes first, so a path under it is not rewritten twice.
    /// </summary>
    public static string Redact(string text, string? userProfile, string? userName, string? machineName)
    {
        if (Usable(userProfile)) text = text.Replace(userProfile!.TrimEnd('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (Usable(userName)) text = text.Replace(userName!, "<user>", StringComparison.OrdinalIgnoreCase);
        if (Usable(machineName)) text = text.Replace(machineName!, "<machine>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>Redacts with this machine's own profile folder, user name and machine name.</summary>
    public static string RedactForSending(string text)
        => Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);

    private static bool Usable(string? value) => value is { Length: >= MinNameLength };
}
