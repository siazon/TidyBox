namespace PowerTools.Modules.DiskAnalyzer.Steam;

public class SteamLibraryParser
{
    /// <summary>Library paths from steamapps/libraryfolders.vdf; empty if the file is missing or unreadable.</summary>
    public IReadOnlyList<string> FindLibraryFolders(string steamInstallPath)
    {
        var file = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) return Array.Empty<string>();

        try
        {
            var root = VdfParser.Parse(File.ReadAllText(file));
            if (!root.TryGetValue("libraryfolders", out var node) || node is not Dictionary<string, object> folders)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (var (key, value) in folders)
            {
                if (key.Length == 0 || !key.All(char.IsDigit)) continue; // old format also stores stats keys here

                // New format: "0" { "path" "..." }. Old format: "1" "D:\\SteamLibrary".
                var path = value switch
                {
                    Dictionary<string, object> d when d.TryGetValue("path", out var p) => p as string,
                    string s => s,
                    _ => null,
                };
                if (!string.IsNullOrEmpty(path)) result.Add(path);
            }

            return result;
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Games from steamapps/appmanifest_*.acf; a manifest that is corrupt or incomplete is skipped.</summary>
    public IReadOnlyList<SteamGameInfo> ParseLibrary(string libraryFolderPath)
    {
        var steamApps = Path.Combine(libraryFolderPath, "steamapps");
        if (!Directory.Exists(steamApps)) return Array.Empty<SteamGameInfo>();

        var games = new List<SteamGameInfo>();
        foreach (var file in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
        {
            try
            {
                games.Add(ParseManifest(file, libraryFolderPath));
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                // Typically a game that is mid-update; ignore this one and keep the rest.
            }
        }

        return games;
    }

    private static SteamGameInfo ParseManifest(string file, string libraryFolderPath)
    {
        var root = VdfParser.Parse(File.ReadAllText(file));
        if (!root.TryGetValue("AppState", out var node) || node is not Dictionary<string, object> app)
        {
            throw new FormatException("no AppState block");
        }

        var installDir = app.TryGetValue("installdir", out var dir) ? dir as string : null;
        return new SteamGameInfo(
            AppId: Text(app, "appid"),
            Name: Text(app, "name"),
            SizeBytes: Number(app, "SizeOnDisk"),
            LastUpdatedUtc: DateTimeOffset.FromUnixTimeSeconds(Number(app, "LastUpdated")).UtcDateTime,
            InstallPath: string.IsNullOrEmpty(installDir) ? "" : Path.Combine(libraryFolderPath, "steamapps", "common", installDir));
    }

    private static string Text(Dictionary<string, object> app, string key)
        => app.TryGetValue(key, out var v) && v is string s && s.Length > 0 ? s : throw new FormatException($"missing '{key}'");

    private static long Number(Dictionary<string, object> app, string key)
        => long.TryParse(Text(app, key), out var n) ? n : throw new FormatException($"'{key}' is not a number");
}
