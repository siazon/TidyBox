using Microsoft.Win32;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;

namespace PowerTools.Modules.ContextMenu.Tests.Support;

public enum Hive { Machine, User }

/// <summary>
/// A fake pair of HKLM/HKCU roots living under HKCU\Software\PowerToolsTests\&lt;guid&gt;, so tests exercise the real
/// registry API without touching real system or user settings. Disposing removes everything it created.
/// </summary>
public sealed class RegistrySandbox : IDisposable
{
    private readonly string _path = @"Software\PowerToolsTests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly RegistryKey _machine;
    private readonly RegistryKey _user;

    public RegistrySandbox()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
        _machine = _root.CreateSubKey("machine");
        _user = _root.CreateSubKey("user");
        Roots = new ContextMenuRegistryRoots(_machine, _user);
    }

    public ContextMenuRegistryRoots Roots { get; }

    public string SandboxPath => _path;

    public RegistryKey Hive(Hive hive) => hive == Support.Hive.Machine ? _machine : _user;

    public void AddHandler(Hive hive, MenuLocation location, string keyName, string? defaultValue)
    {
        using var key = Hive(hive).CreateSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\{location.RegistryKeyName()}\shellex\ContextMenuHandlers\{keyName}");
        if (defaultValue is not null) key.SetValue("", defaultValue);
    }

    public void AddVerb(Hive hive, MenuLocation location, string verbName, params (string Name, object Value)[] values)
    {
        using var key = Hive(hive).CreateSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\{location.RegistryKeyName()}\shell\{verbName}");
        foreach (var (name, value) in values) key.SetValue(name, value);
    }

    public void RegisterClsid(Hive hive, string clsid, string? name, string? dllPath)
    {
        using var key = Hive(hive).CreateSubKey($@"{ContextMenuRegistryRoots.ClassesPath}\CLSID\{clsid}");
        if (name is not null) key.SetValue("", name);
        if (dllPath is not null)
        {
            using var server = key.CreateSubKey("InprocServer32");
            server.SetValue("", dllPath);
        }
    }

    public void Block(Hive hive, string clsid)
    {
        using var key = Hive(hive).CreateSubKey(ContextMenuRegistryRoots.BlockedPath);
        key.SetValue(clsid, "");
    }

    public void Dispose()
    {
        _machine.Close();
        _user.Close();
        _root.Close();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }
}
