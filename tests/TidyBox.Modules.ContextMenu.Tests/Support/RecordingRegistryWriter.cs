using Microsoft.Win32;
using TidyBox.Modules.ContextMenu.Hives;

namespace TidyBox.Modules.ContextMenu.Tests.Support;

/// <summary>Remembers what was asked of the registry writer. With no inner writer nothing is changed at all.</summary>
public sealed class RecordingRegistryWriter(IRegistryWriter? inner = null) : IRegistryWriter
{
    public List<string> Calls { get; } = new();

    public void SetString(RegistryKey root, string path, string? valueName, string value)
    {
        Calls.Add($"SetString|{path}|{valueName ?? "(default)"}|{value}");
        inner?.SetString(root, path, valueName, value);
    }

    public void DeleteValue(RegistryKey root, string path, string valueName)
    {
        Calls.Add($"DeleteValue|{path}|{valueName}");
        inner?.DeleteValue(root, path, valueName);
    }

    public void DeleteKey(RegistryKey root, string path)
    {
        Calls.Add($"DeleteKey|{path}");
        inner?.DeleteKey(root, path);
    }
}
