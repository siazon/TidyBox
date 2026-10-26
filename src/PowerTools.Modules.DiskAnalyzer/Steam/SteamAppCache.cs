using System.Text.Json;

namespace PowerTools.Modules.DiskAnalyzer.Steam;

/// <summary>
/// Remembers appid -> game name across sessions, so a game's name can still be shown after it has been
/// uninstalled and its appmanifest is gone (e.g. for leftover workshop content). Local-only JSON file, not synced
/// or uploaded anywhere. A missing or corrupt file is treated as an empty cache rather than failing the caller.
/// </summary>
public sealed class SteamAppCache
{
    private readonly string _filePath;
    private readonly object _gate = new();

    public SteamAppCache(string? filePath = null)
    {
        _filePath = filePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerTools", "SteamAppCache.json");
    }

    /// <returns>The last known name for this appid, or null if it was never seen.</returns>
    public string? TryGetName(string appId) => Load().TryGetValue(appId, out var name) ? name : null;

    /// <summary>Remembers (or overwrites) the name for this appid.</summary>
    public void Upsert(string appId, string name)
    {
        lock (_gate)
        {
            var entries = Load();
            entries[appId] = name;
            Save(entries);
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable cache just means "nothing remembered yet"; it is a convenience, not a source of truth.
            return new();
        }
    }

    private void Save(Dictionary<string, string> entries)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(entries));
    }
}
