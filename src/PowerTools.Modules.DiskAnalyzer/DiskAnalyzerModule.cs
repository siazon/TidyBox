using Microsoft.Win32;
using PowerTools.Core.Modules;

namespace PowerTools.Modules.DiskAnalyzer;

public class DiskAnalyzerModule : IToolModule
{
    private readonly string? _steamInstallPathOverride;

    public DiskAnalyzerModule(string? steamInstallPathOverride = null)
    {
        _steamInstallPathOverride = steamInstallPathOverride;
    }

    public string ModuleId => "disk-analyzer";

    public string DisplayName => "磁盘空间分析";

    /// <summary>False until <see cref="InitializeAsync"/> has run, and when Steam is not installed.</summary>
    public bool IsSteamAvailable => SteamInstallPath is not null;

    public string? SteamInstallPath { get; private set; }

    public Task InitializeAsync()
    {
        // A machine without Steam is a normal state, not an error: the Steam view just stays empty.
        var candidate = _steamInstallPathOverride ?? DetectSteamInstallPath();
        SteamInstallPath = candidate is not null && Directory.Exists(Path.Combine(candidate, "steamapps")) ? candidate : null;
        return Task.CompletedTask;
    }

    private static string? DetectSteamInstallPath()
    {
        // Steam records its install folder (with forward slashes) here; fall back to the default location.
        var fromRegistry = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(fromRegistry)) return fromRegistry.Replace('/', '\\');

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }
}
