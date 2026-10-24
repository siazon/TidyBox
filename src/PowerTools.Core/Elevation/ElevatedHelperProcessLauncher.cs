using System.Diagnostics;
using System.Security.Principal;

namespace PowerTools.Core.Elevation;

public class ElevatedHelperProcessLauncher : IHelperProcessLauncher
{
    private readonly string _helperExePath;

    public ElevatedHelperProcessLauncher(string helperExePath)
    {
        _helperExePath = helperExePath;
    }

    public bool IsHelperRunning(string pipeName) => File.Exists(@"\.\pipe\" + pipeName);

    // The helper may run as a different admin account, so tell it which SID (ours) may connect to the pipe.
    // Throws Win32Exception if the user declines the UAC prompt; ElevationClient turns that into a failure response.
    public void LaunchElevated(string pipeName)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _helperExePath,
            Arguments = $"{pipeName} {WindowsIdentity.GetCurrent().User!.Value}",
            UseShellExecute = true,
            Verb = "runas",
        });
    }
}
