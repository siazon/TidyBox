using System.Diagnostics;

namespace TidyBox.Modules.ContextMenu.Naming;

public record ModuleInfo(string? Description, string? Company);

public static class ModuleInfoReader
{
    /// <summary>Description and company from a DLL's version resource; null if the file is missing or unreadable.</summary>
    public static ModuleInfo? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return new ModuleInfo(Clean(info.FileDescription), Clean(info.CompanyName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
