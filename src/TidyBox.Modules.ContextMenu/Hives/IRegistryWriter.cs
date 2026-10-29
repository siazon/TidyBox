using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace TidyBox.Modules.ContextMenu.Hives;

/// <summary>
/// Changes the registry. Every path is relative to <paramref name="root"/>. A failure is reported as an
/// <see cref="IOException"/>; asking to remove something that is not there is not a failure.
/// </summary>
public interface IRegistryWriter
{
    /// <param name="valueName">The value to write; null for the key's default value.</param>
    void SetString(RegistryKey root, string path, string? valueName, string value);

    void DeleteValue(RegistryKey root, string path, string valueName);

    /// <summary>Removes the key and everything under it.</summary>
    void DeleteKey(RegistryKey root, string path);
}

/// <summary>
/// Writes through reg.exe on purpose. A packaged app's own registry calls are virtualized (they land in a private copy
/// that Explorer never sees) unless the manifest declares the restricted capability "unvirtualizedResources", which the
/// Store is not meant to grant to a utility like this. A child process's changes go to the real registry.
/// </summary>
public sealed class RegExeRegistryWriter : IRegistryWriter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public void SetString(RegistryKey root, string path, string? valueName, string value)
    {
        var args = new List<string> { "add", FullName(root, path) };
        if (valueName is null) args.Add("/ve");
        else args.AddRange(new[] { "/v", valueName });
        args.AddRange(new[] { "/t", "REG_SZ", "/d", value, "/f", "/reg:64" });
        Run(args);
    }

    public void DeleteValue(RegistryKey root, string path, string valueName)
    {
        using (var key = root.OpenSubKey(path))
        {
            if (key?.GetValue(valueName) is null) return;
        }

        Run(new[] { "delete", FullName(root, path), "/v", valueName, "/f", "/reg:64" });
    }

    public void DeleteKey(RegistryKey root, string path)
    {
        using (var key = root.OpenSubKey(path))
        {
            if (key is null) return;
        }

        Run(new[] { "delete", FullName(root, path), "/f", "/reg:64" });
    }

    private static string FullName(RegistryKey root, string path) => root.Name + @"\" + path;

    private static void Run(IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // An argument list, never a command line string: a key or value name must not be able to add switches or commands.
        foreach (var arg in args) start.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Could not start reg.exe.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                throw new IOException("reg.exe did not finish in time.");
            }

            Task.WaitAll(output, error);
            if (process.ExitCode != 0) throw new IOException($"reg.exe returned {process.ExitCode}: {error.Result.Trim()}");
        }
        catch (Win32Exception ex)
        {
            throw new IOException("Could not start reg.exe: " + ex.Message, ex);
        }
    }
}
