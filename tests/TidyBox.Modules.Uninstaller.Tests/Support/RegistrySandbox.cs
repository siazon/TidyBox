using Microsoft.Win32;
using TidyBox.Modules.Uninstaller.Catalog;

namespace TidyBox.Modules.Uninstaller.Tests.Support;

/// <summary>
/// Three fake Uninstall roots (HKLM 64-bit, HKLM 32-bit, HKCU) living under HKCU\Software\TidyBoxTests\&lt;guid&gt;.
/// Disposing removes everything it created.
/// </summary>
public sealed class RegistrySandbox : IDisposable
{
    private readonly string _path = @"Software\TidyBoxTests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;

    public RegistrySandbox()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
        Machine64 = _root.CreateSubKey("machine64");
        Machine32 = _root.CreateSubKey("machine32");
        User = _root.CreateSubKey("user");
    }

    public RegistryKey Machine64 { get; }

    public RegistryKey Machine32 { get; }

    public RegistryKey User { get; }

    public Win32AppCatalog Catalog() => new(Machine64, Machine32, User);

    /// <summary>Creates an Uninstall entry under the given fake root.</summary>
    public static void AddEntry(RegistryKey root, string keyName, params (string Name, object Value)[] values)
    {
        using var key = root.CreateSubKey(Win32AppCatalog.UninstallPath + @"\" + keyName);
        foreach (var (name, value) in values) key.SetValue(name, value);
    }

    public void Dispose()
    {
        Machine64.Dispose();
        Machine32.Dispose();
        User.Dispose();
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }
}
