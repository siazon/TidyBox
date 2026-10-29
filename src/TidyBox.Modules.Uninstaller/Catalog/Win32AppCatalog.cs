using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Catalog;

/// <summary>Installed desktop programs from the three Uninstall registry roots; Steam games show up here too.</summary>
public sealed class Win32AppCatalog : IAppCatalog
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly Regex SteamUninstall = new(@"steam://uninstall/(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly (string Prefix, RegistryKey Root)[] _roots;

    /// <param name="machine64">HKLM as seen by 64-bit programs.</param>
    /// <param name="machine32">HKLM as seen by 32-bit programs (same relative path, redirected by the registry view).</param>
    public Win32AppCatalog(RegistryKey machine64, RegistryKey machine32, RegistryKey user)
    {
        _roots = new[] { ("HKLM64", machine64), ("HKLM32", machine32), ("HKCU", user) };
    }

    // This app is a 32-bit process: without an explicit 64-bit view the native HKLM entries would be redirected away.
    public static Win32AppCatalog Real() => new(
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32),
        Registry.CurrentUser);

    public IReadOnlyList<InstalledApp> LoadAll()
    {
        var apps = new List<InstalledApp>();
        foreach (var (prefix, root) in _roots)
        {
            try
            {
                using var uninstall = root.OpenSubKey(UninstallPath);
                if (uninstall is null) continue;

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    var app = TryRead(uninstall, prefix, name);
                    if (app is not null) apps.Add(app);
                }
            }
            catch (Exception ex) when (IsRegistryFailure(ex))
            {
                // A root that cannot be read just contributes nothing.
            }
        }

        return apps.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A single-key lookup, cheap enough to poll while an uninstaller is still working.</summary>
    public bool? IsInstalled(InstalledApp app)
    {
        if (app.Source == AppSource.Store) return null;

        var separator = app.Id.IndexOf('\\');
        if (separator <= 0) return null;

        var root = _roots.FirstOrDefault(r => r.Prefix == app.Id.Substring(0, separator));
        if (root.Root is null) return null;

        try
        {
            using var key = root.Root.OpenSubKey(UninstallPath + "\\" + app.Id.Substring(separator + 1));
            return key is not null;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return null;
        }
    }

    private static InstalledApp? TryRead(RegistryKey uninstall, string prefix, string name)
    {
        try
        {
            using var key = uninstall.OpenSubKey(name);
            if (key is null) return null;

            if (key.GetValue("DisplayName") is not string { Length: > 0 } displayName) return null;
            if (key.GetValue("SystemComponent") is 1) return null;
            if (key.GetValue("ParentKeyName") is string { Length: > 0 }) return null;
            if (key.GetValue("ReleaseType") is string releaseType
                && releaseType is "Update" or "Hotfix" or "Security Update" or "Service Pack") return null;

            var uninstallString = Text(key, "UninstallString");
            var steam = uninstallString is null ? null : SteamUninstall.Match(uninstallString);
            var steamId = steam is { Success: true } ? steam.Groups[1].Value : null;

            return new InstalledApp(
                Id: $@"{prefix}\{name}",
                DisplayName: displayName,
                Publisher: Text(key, "Publisher"),
                Version: Text(key, "DisplayVersion"),
                EstimatedSizeBytes: key.GetValue("EstimatedSize") is int kb and >= 0 ? kb * 1024L : null,
                InstalledOn: DateTime.TryParseExact(Text(key, "InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
                Source: steamId is null ? AppSource.Win32 : AppSource.Steam,
                InstallLocation: Text(key, "InstallLocation"),
                UninstallString: uninstallString,
                SteamAppId: steamId,
                PackageFullName: null)
            {
                DisplayIcon = Text(key, "DisplayIcon"),
            };
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return null;
        }
    }

    private static string? Text(RegistryKey key, string name)
        => key.GetValue(name) is string { Length: > 0 } s ? s : null;

    private static bool IsRegistryFailure(Exception ex)
        => ex is UnauthorizedAccessException or IOException or SecurityException or ObjectDisposedException;
}
