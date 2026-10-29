using System.Diagnostics;
using Windows.Management.Deployment;

namespace TidyBox.Modules.Uninstaller.Routing;

public interface IProcessRunner
{
    Task<int> RunAndWaitAsync(string fileName, string arguments, CancellationToken ct, bool asAdministrator = false);
}

public interface IStoreRemover
{
    Task RemoveAsync(string packageFullName, CancellationToken ct);
}

public interface IUrlLauncher
{
    void Open(Uri uri);
}

/// <summary>Starts the program through the shell, so an uninstaller that needs administrator rights shows its own UAC prompt.</summary>
public sealed class ShellProcessRunner : IProcessRunner
{
    public async Task<int> RunAndWaitAsync(string fileName, string arguments, CancellationToken ct, bool asAdministrator = false)
    {
        var start = new ProcessStartInfo(fileName, arguments) { UseShellExecute = true };
        if (asAdministrator) start.Verb = "runas";
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}

public sealed class PackageManagerRemover : IStoreRemover
{
    public async Task RemoveAsync(string packageFullName, CancellationToken ct)
    {
        var result = await new PackageManager().RemovePackageAsync(packageFullName).AsTask(ct);
        if (result.ExtendedErrorCode is { } error) throw new InvalidOperationException(result.ErrorText, error);
    }
}

public sealed class ShellUrlLauncher : IUrlLauncher
{
    public void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
}
