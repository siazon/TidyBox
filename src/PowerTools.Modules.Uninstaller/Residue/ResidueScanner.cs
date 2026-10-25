using System.Security;
using System.Text.RegularExpressions;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Residue;

/// <summary>What looked like it belonged to the app before the uninstall ran. Opaque; hand it back to <see cref="ResidueScanner.FindLeftovers"/>.</summary>
public sealed class ResidueSnapshot
{
    internal ResidueSnapshot(IReadOnlySet<string> keys) => Keys = keys;

    internal IReadOnlySet<string> Keys { get; }
}

/// <summary>
/// Finds what an uninstaller left behind. The rule is "better to miss something than to point at somebody else's data":
/// only the app's own install location and folders/keys named exactly like the app (or like it under the publisher's
/// folder) count, and only if they were there before the uninstall and are still there after it.
/// </summary>
public sealed class ResidueScanner
{
    // Words that name a kind of software rather than one product; a folder called "Setup" or "Office" is not ours to claim.
    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "update", "setup", "tools", "tool", "app", "apps", "client", "desktop", "server", "software", "system",
        "service", "runtime", "driver", "utility", "installer", "office", "java",
    };

    private static readonly Regex DottedVersion = new(@"\bv?\d+(\.\d+)+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Parenthetical = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Architecture = new(@"\b(x64|x86|amd64|64-bit|32-bit)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ResidueRoots _roots;

    public ResidueScanner(ResidueRoots roots) => _roots = roots;

    public ResidueSnapshot Capture(InstalledApp app) => new(Candidates(app).Select(Key).ToHashSet(StringComparer.Ordinal));

    public IReadOnlyList<ResidueItem> FindLeftovers(InstalledApp app, ResidueSnapshot before)
        => Candidates(app)
            .Where(c => before.Keys.Contains(Key(c)))
            .OrderBy(c => c.Confidence)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Key(ResidueItem item) => $"{item.Kind}|{item.Path.ToLowerInvariant()}";

    private List<ResidueItem> Candidates(InstalledApp app)
    {
        var found = new Dictionary<string, ResidueItem>(StringComparer.Ordinal);
        void Add(ResidueItem item) => found.TryAdd(Key(item), item);

        if (InstallLocationCandidate(app) is { } own) Add(own);

        var appName = Normalize(app.DisplayName);
        var publisher = Normalize(app.Publisher);
        var matchByName = Eligible(appName);
        var matchByPublisher = Eligible(publisher);

        if (matchByName)
        {
            foreach (var item in NameMatchesInFolders(appName, matchByPublisher ? publisher : null)) Add(item);
            foreach (var item in NameMatchesInRegistry(appName, matchByPublisher ? publisher : null)) Add(item);
        }

        return found.Values.ToList();
    }

    private ResidueItem? InstallLocationCandidate(InstalledApp app)
    {
        if (string.IsNullOrWhiteSpace(app.InstallLocation)) return null;

        string path;
        try
        {
            path = ResidueRoots.FullPath(app.InstallLocation);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!Directory.Exists(path) || _roots.IsProtectedFolder(path)) return null;

        return FolderItem(path, ResidueConfidence.High, "安装目录");
    }

    private IEnumerable<ResidueItem> NameMatchesInFolders(string appName, string? publisher)
    {
        foreach (var root in _roots.FolderRoots)
        {
            foreach (var child in SafeDirectories(root))
            {
                var name = Normalize(System.IO.Path.GetFileName(child));
                if (name == appName)
                {
                    if (!_roots.IsProtectedFolder(child)) yield return FolderItem(child, ResidueConfidence.Medium, "名称与应用相同");
                }
                else if (publisher is not null && name == publisher)
                {
                    foreach (var grandchild in SafeDirectories(child))
                    {
                        if (Normalize(System.IO.Path.GetFileName(grandchild)) == appName && !_roots.IsProtectedFolder(grandchild))
                        {
                            yield return FolderItem(grandchild, ResidueConfidence.Medium, "位于发布者目录下，名称与应用相同");
                        }
                    }
                }
            }
        }
    }

    private IEnumerable<ResidueItem> NameMatchesInRegistry(string appName, string? publisher)
    {
        foreach (var root in _roots.RegistryRoots)
        {
            foreach (var name in SafeSubKeyNames(root, root.Path))
            {
                var normalized = Normalize(name);
                if (normalized == appName)
                {
                    if (!ResidueRoots.ProtectedNames.Contains(normalized)) yield return RegistryItem(root, $@"{root.Path}\{name}", "名称与应用相同");
                }
                else if (publisher is not null && normalized == publisher)
                {
                    foreach (var sub in SafeSubKeyNames(root, $@"{root.Path}\{name}"))
                    {
                        if (Normalize(sub) == appName) yield return RegistryItem(root, $@"{root.Path}\{name}\{sub}", "位于发布者项下，名称与应用相同");
                    }
                }
            }
        }
    }

    private ResidueItem FolderItem(string path, ResidueConfidence confidence, string reason)
        => new(ResidueKind.Folder, path, confidence, reason,
            NeedsElevation: _roots.ElevatedFolderRoots.Any(r => ResidueRoots.IsUnder(path, ResidueRoots.FullPath(r))),
            Removable: true);

    private static ResidueItem RegistryItem(RegistryRoot root, string relativePath, string reason)
        => new(ResidueKind.RegistryKey, $@"{root.Hive}\{relativePath}", ResidueConfidence.Medium, reason, root.NeedsElevation, Removable: true);

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            // A junction or symlink is somebody else's data reached through our path; never offer it.
            return Directory.EnumerateDirectories(folder)
                .Where(d => !new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint))
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeSubKeyNames(RegistryRoot root, string path)
    {
        try
        {
            using var key = root.Key.OpenSubKey(path);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException or ObjectDisposedException)
        {
            return Array.Empty<string>();
        }
    }

    // Too short, a word like "Setup", or something Windows owns: not enough to say a folder belongs to this app.
    internal static bool Eligible(string normalized)
        => (normalized.Length > 3 || normalized.Any(c => c >= 0x4E00))
           && !GenericNames.Contains(normalized)
           && !ResidueRoots.ProtectedNames.Contains(normalized);

    /// <summary>Lower-case letters and digits only, with version numbers, architecture tags and parenthesized notes removed.</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        text = Parenthetical.Replace(text, " ");
        text = DottedVersion.Replace(text, " ");
        text = Architecture.Replace(text, " ");
        return new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
