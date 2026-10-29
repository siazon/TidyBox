using System.Diagnostics;

namespace TidyBox.Modules.Downloads.Tests;

/// <summary>Creates links for tests. Symbolic links need developer mode or elevation; a directory junction (also a reparse point) does not.</summary>
internal static class TestLinks
{
    public static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var junction = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        });
        junction!.WaitForExit();
        return junction.ExitCode == 0 && Directory.Exists(link);
    }

    public static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
