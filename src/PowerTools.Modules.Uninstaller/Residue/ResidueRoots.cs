using Microsoft.Win32;

namespace PowerTools.Modules.Uninstaller.Residue;

/// <param name="Hive">Prefix shown in paths, e.g. "HKCU".</param>
/// <param name="Path">Key below <paramref name="Key"/> whose direct subkeys are inspected, e.g. "Software".</param>
public record RegistryRoot(string Hive, RegistryKey Key, string Path, bool NeedsElevation);

/// <summary>Where leftovers are looked for, and what must never be touched.</summary>
public sealed class ResidueRoots
{
    /// <summary>Folder or key names (normalized) that belong to Windows or to many apps at once.</summary>
    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "microsoft", "windows", "windowsnt", "windowsapps", "commonfiles", "packages", "classes", "policies",
        "wow6432node", "programs", "microsoftshared", "internetexplorer", "windowspowershell", "dotnet", "temp",
        "clients", "registeredapplications", "odbc",
    };

    public ResidueRoots(IReadOnlyList<string> folderRoots, IReadOnlyList<string> elevatedFolderRoots, IReadOnlyList<RegistryRoot> registryRoots)
    {
        FolderRoots = folderRoots;
        ElevatedFolderRoots = elevatedFolderRoots;
        RegistryRoots = registryRoots;
    }

    public IReadOnlyList<string> FolderRoots { get; }

    /// <summary>The subset of <see cref="FolderRoots"/> a normal user cannot write to.</summary>
    public IReadOnlyList<string> ElevatedFolderRoots { get; }

    public IReadOnlyList<RegistryRoot> RegistryRoots { get; }

    /// <summary>True for anything that must never be offered or removed: a drive root, one of our roots, the Windows folder, or a protected name.</summary>
    public bool IsProtectedFolder(string path)
    {
        if (Path.GetPathRoot(path) is { } driveRoot && SamePath(path, driveRoot)) return true;
        if (ProtectedNames.Contains(ResidueScanner.Normalize(Path.GetFileName(FullPath(path))))) return true;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0 && (SamePath(path, windows) || IsUnder(FullPath(path), FullPath(windows)))) return true;

        return FolderRoots.Concat(ElevatedFolderRoots).Any(r => SamePath(path, r));
    }

    /// <summary>
    /// True only for a key directly below one of the registry roots, or one level deeper (Vendor\App), whose first
    /// name is not a protected one. The roots themselves and anything else are never removable.
    /// </summary>
    public bool IsRemovableRegistryKey(string path)
    {
        foreach (var root in RegistryRoots)
        {
            var prefix = $@"{root.Hive}\{root.Path}\";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var segments = path.Substring(prefix.Length).Split('\\');
            if (segments.Length is < 1 or > 2) continue;
            if (segments.Any(s => s.Length == 0 || s == "." || s == "..")) continue;
            if (ProtectedNames.Contains(ResidueScanner.Normalize(segments[0]))) continue;

            return true;
        }

        return false;
    }

    /// <summary>Whether the key is still there, as far as can be told; when it cannot be read it counts as there, so the tool gets to decide.</summary>
    public bool RegistryKeyExists(string path)
    {
        foreach (var root in RegistryRoots)
        {
            var prefix = $@"{root.Hive}\";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var relative = path.Substring(prefix.Length);
            if (!relative.StartsWith(root.Path + "\\", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var key = root.Key.OpenSubKey(relative);
                return key is not null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
            {
                return true;
            }
        }

        return true;
    }

    internal static string FullPath(string path) => Path.GetFullPath(path).TrimEnd('\\');

    internal static bool IsUnder(string path, string root) => path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) => string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

    public static ResidueRoots Real()
    {
        static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

        var elevated = new[]
        {
            // A 32-bit process is told that "Program Files" is the x86 folder; ProgramW6432 always names the native one.
            Environment.GetEnvironmentVariable("ProgramW6432") ?? Folder(Environment.SpecialFolder.ProgramFiles),
            Folder(Environment.SpecialFolder.ProgramFilesX86),
            Folder(Environment.SpecialFolder.CommonApplicationData),
        };
        var user = new[]
        {
            Folder(Environment.SpecialFolder.LocalApplicationData),
            Folder(Environment.SpecialFolder.ApplicationData),
        };

        var machine64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return new ResidueRoots(
            folderRoots: elevated.Concat(user).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            elevatedFolderRoots: elevated.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            registryRoots: new[]
            {
                new RegistryRoot("HKCU", Registry.CurrentUser, "Software", NeedsElevation: false),
                new RegistryRoot("HKLM", machine64, "SOFTWARE", NeedsElevation: true),
                new RegistryRoot("HKLM", machine64, @"SOFTWARE\WOW6432Node", NeedsElevation: true),
            });
    }
}
