using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PowerTools.ElevatedHelper;

/// <summary>Runs reg.exe. The helper changes the registry through the system tool so the change reaches the real registry.</summary>
public interface IRegistryTool
{
    /// <returns>reg.exe's exit code (0 = success).</returns>
    int Run(IReadOnlyList<string> args);
}

public sealed class ProcessRegistryTool : IRegistryTool
{
    public int Run(IReadOnlyList<string> args)
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
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
