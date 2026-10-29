namespace TidyBox.Modules.Uninstaller.Model;

public enum AppSource { Win32, Store, Steam }

/// <param name="Id">Unique within a source: "HKLM64\key", "HKLM32\key", "HKCU\key" for Win32/Steam, the package full name for Store.</param>
public record InstalledApp(
    string Id,
    string DisplayName,
    string? Publisher,
    string? Version,
    long? EstimatedSizeBytes,
    DateTime? InstalledOn,
    AppSource Source,
    string? InstallLocation,
    string? UninstallString,
    string? SteamAppId,
    string? PackageFullName)
{
    /// <summary>The raw DisplayIcon registry value, e.g. "C:\x\y.exe",0 — where the program keeps its icon.</summary>
    public string? DisplayIcon { get; init; }
}
