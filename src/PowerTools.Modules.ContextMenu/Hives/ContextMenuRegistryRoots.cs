using Microsoft.Win32;

namespace PowerTools.Modules.ContextMenu.Hives;

/// <summary>
/// The two registry roots the module reads and writes. They are injectable so tests can point them at a sandbox
/// under HKCU; the relative paths below are the same in every root.
/// </summary>
public sealed class ContextMenuRegistryRoots
{
    public const string ClassesPath = @"Software\Classes";
    public const string BlockedPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public ContextMenuRegistryRoots(RegistryKey machine, RegistryKey user)
    {
        Machine = machine;
        User = user;
    }

    public RegistryKey Machine { get; }

    public RegistryKey User { get; }

    /// <summary>
    /// The real hives, always in the native 64-bit view. Explorer is 64-bit, so what it reads is what must be written;
    /// the default view of a 32-bit process would silently be redirected to Wow6432Node.
    /// </summary>
    public static ContextMenuRegistryRoots Real() => new(
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64));
}
