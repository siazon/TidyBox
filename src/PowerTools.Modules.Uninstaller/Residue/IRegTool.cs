using System.Diagnostics;

namespace PowerTools.Modules.Uninstaller.Residue;

/// <summary>
/// Runs reg.exe. The registry is changed through a child process on purpose: a packaged app's own registry calls
/// are virtualized and never reach the real registry, while the system tool's changes do.
/// </summary>
public interface IRegTool
{
    /// <returns>reg.exe's exit code (0 = success).</returns>
    Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct);
}

public sealed class ProcessRegTool : IRegTool
{
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // An argument list, never a command line string: a key name must not be able to add switches or commands.
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start reg.exe.");
        await Task.WhenAll(process.StandardOutput.ReadToEndAsync(ct), process.StandardError.ReadToEndAsync(ct), process.WaitForExitAsync(ct));
        return process.ExitCode;
    }
}
