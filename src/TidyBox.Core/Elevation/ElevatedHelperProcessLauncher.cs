using System.Diagnostics;
using System.Security.Principal;

namespace TidyBox.Core.Elevation;

public class ElevatedHelperProcessLauncher : IHelperProcessLauncher
{
    private readonly string _helperExePath;

    public ElevatedHelperProcessLauncher(string helperExePath)
    {
        _helperExePath = helperExePath;
    }

    public bool IsHelperRunning(string pipeName) => PipeExists(pipeName);

    // Lists the pipe directory instead of File.Exists: File.Exists opens the pipe, which connects to (and burns)
    // a listening instance and can make the server's pending accept fail.
    public static bool PipeExists(string pipeName)
        => Directory.EnumerateFiles(@"\\.\pipe\").Any(f => string.Equals(Path.GetFileName(f), pipeName, StringComparison.OrdinalIgnoreCase));

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
            // The helper is a console program with nothing to show; without this a black window stays open while it runs.
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
