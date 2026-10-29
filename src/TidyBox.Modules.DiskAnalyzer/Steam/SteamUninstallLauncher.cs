using System.Diagnostics;

namespace TidyBox.Modules.DiskAnalyzer.Steam;

public interface ISteamUninstallLauncher
{
    void LaunchUninstall(string appId);
}

/// <summary>Hands the uninstall to Steam itself via its URL protocol; no elevation needed.</summary>
public class SteamUninstallLauncher : ISteamUninstallLauncher
{
    public void LaunchUninstall(string appId)
    {
        Process.Start(new ProcessStartInfo(BuildUri(appId).ToString()) { UseShellExecute = true });
    }

    // The app id comes from a manifest file on disk and ends up in a ShellExecute call, so accept digits only.
    internal static Uri BuildUri(string appId)
    {
        if (string.IsNullOrEmpty(appId) || !appId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException($"invalid Steam app id: '{appId}'", nameof(appId));
        }

        return new Uri("steam://uninstall/" + appId);
    }
}
