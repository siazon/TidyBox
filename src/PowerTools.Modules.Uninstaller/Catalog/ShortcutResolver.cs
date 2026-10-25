using System.Runtime.InteropServices;

namespace PowerTools.Modules.Uninstaller.Catalog;

public static class ShortcutResolver
{
    /// <returns>The shortcut's target, the path itself when it is not a shortcut, or null when the shortcut cannot be read.</returns>
    public static string? Resolve(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;

        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            string target = shell.CreateShortcut(path).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }
}
