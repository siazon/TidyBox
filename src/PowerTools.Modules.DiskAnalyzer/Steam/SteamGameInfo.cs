namespace PowerTools.Modules.DiskAnalyzer.Steam;

/// <param name="InstallPath">Empty when the manifest does not name an install directory.</param>
public record SteamGameInfo(string AppId, string Name, long SizeBytes, DateTime LastUpdatedUtc, string InstallPath);
