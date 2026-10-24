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

    public static ContextMenuRegistryRoots Real() => new(Registry.LocalMachine, Registry.CurrentUser);
}
