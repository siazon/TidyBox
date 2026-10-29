using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace TidyBox.App;

internal static class AppVersion
{
    /// <summary>The package version as "major.minor.build.revision", the form the feedback server expects.</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return "0.0.0.0"; // not running with a package identity
        }
    }
}
