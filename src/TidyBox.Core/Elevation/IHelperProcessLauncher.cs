namespace TidyBox.Core.Elevation;

public interface IHelperProcessLauncher
{
    bool IsHelperRunning(string pipeName);
    void LaunchElevated(string pipeName);
}
