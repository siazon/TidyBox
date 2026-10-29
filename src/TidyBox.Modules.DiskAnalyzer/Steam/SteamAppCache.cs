using System.Text.Json;

namespace TidyBox.Modules.DiskAnalyzer.Steam;

/// <summary>Looks up a game's name by appid; implemented by <see cref="SteamAppCache"/>, fakeable in tests.</summary>
public interface ISteamAppNameLookup
{
    string? TryGetName(string appId);
}

/// <summary>
/// Remembers appid -> game name across sessions, so a game's name can still be shown after it has been
/// uninstalled and its appmanifest is gone (e.g. for leftover workshop content). Local-only JSON file, not synced
/// or uploaded anywhere. A missing or corrupt file is treated as an empty cache rather than failing the caller.
/// </summary>
public sealed class SteamAppCache : ISteamAppNameLookup
{
    private readonly string _filePath;
    private readonly object _gate = new();

    public SteamAppCache(string? filePath = null)
    {
        _filePath = filePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidyBox", "SteamAppCache.json");
    }

    private sealed record Entry(string Name, string? InstallDir);

    /// <returns>The last known name for this appid, or null if it was never seen.</returns>
    public string? TryGetName(string appId) => Load().TryGetValue(appId, out var entry) ? entry.Name : null;

    /// <returns>The appid last seen installed under steamapps/common/&lt;installDir&gt;, or null if none.</returns>
    public string? TryFindAppIdByInstallDir(string installDir)
        => installDir.Length == 0
            ? null
            : Load().FirstOrDefault(kv => string.Equals(kv.Value.InstallDir, installDir, StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>Remembers (or overwrites) the name for this appid; a null/empty <paramref name="installDir"/> keeps the one already stored.</summary>
    public void Upsert(string appId, string name, string? installDir = null)
    {
        lock (_gate)
        {
            var entries = Load();
            var dir = string.IsNullOrEmpty(installDir) ? (entries.TryGetValue(appId, out var old) ? old.InstallDir : null) : installDir;
            entries[appId] = new Entry(name, dir);
            Save(entries);
        }
    }

    private Dictionary<string, Entry> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();

            var entries = new Dictionary<string, Entry>();
            foreach (var (appId, value) in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_filePath)) ?? new())
            {
                // Older files stored just the name as a plain string.
                var entry = value.ValueKind == JsonValueKind.String ? new Entry(value.GetString() ?? "", null) : value.Deserialize<Entry>();
                if (entry is not null) entries[appId] = entry;
            }
            return entries;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable cache just means "nothing remembered yet"; it is a convenience, not a source of truth.
            return new();
        }
    }

    private void Save(Dictionary<string, Entry> entries)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(entries));
    }
}
