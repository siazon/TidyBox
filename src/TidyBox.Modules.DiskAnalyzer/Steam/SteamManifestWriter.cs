namespace TidyBox.Modules.DiskAnalyzer.Steam;

/// <summary>
/// Writes a fresh <c>appmanifest_&lt;appid&gt;.acf</c> for a game folder whose manifest went missing, so Steam
/// recognises the existing files again instead of re-downloading them. Never overwrites an existing manifest.
/// </summary>
public static class SteamManifestWriter
{
    /// <summary>Validates an appid and returns it in canonical form (digits only, no leading zeros).</summary>
    /// <exception cref="ArgumentException">Not a positive 32-bit number; also keeps the value safe to use in a file name.</exception>
    public static string NormalizeAppId(string appId)
    {
        if (!uint.TryParse(appId.Trim(), out var id) || id == 0)
        {
            throw new ArgumentException($"'{appId}' is not a valid Steam appid.", nameof(appId));
        }
        return id.ToString();
    }

    /// <returns>The path of the manifest that was written.</returns>
    /// <exception cref="IOException">A manifest for this appid already exists.</exception>
    public static string Write(string libraryPath, string appId, string name, string installDir, long sizeBytes)
    {
        var id = NormalizeAppId(appId);
        var path = Path.Combine(libraryPath, "steamapps", $"appmanifest_{id}.acf");

        // The fields SteamLibraryParser requires; StateFlags 4 = fully installed. Steam fills in the rest
        // (build id, depots) itself when it next checks the game.
        var content = $$"""
            "AppState"
            {
            	"appid"		"{{id}}"
            	"Universe"		"1"
            	"name"		"{{Escape(name)}}"
            	"StateFlags"		"4"
            	"installdir"		"{{Escape(installDir)}}"
            	"LastUpdated"		"{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}"
            	"SizeOnDisk"		"{{sizeBytes}}"
            }
            """;

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        return path;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
