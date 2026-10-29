using System.Text.Json;
using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Review;

public sealed record KeptEntry(string FullPath, long SizeBytes, DateTime ModifiedUtc);

/// <summary>
/// Files the user chose to keep ("保留，不再提醒"). A file counts as kept only while its path, size and modified time all still match what was
/// recorded, so a file that has changed comes back into the review. Every change is written to disk at once.
/// </summary>
public sealed class KeptStore
{
    private readonly string _filePath;
    private readonly List<KeptEntry> _entries;

    public KeptStore(string filePath)
    {
        _filePath = filePath;
        _entries = Load();
    }

    public bool IsKept(DownloadFile file) => _entries.Any(entry => Matches(entry, file));

    public void Keep(DownloadFile file)
    {
        _entries.RemoveAll(entry => SamePath(entry.FullPath, file.FullPath));
        _entries.Add(new KeptEntry(file.FullPath, file.SizeBytes, file.ModifiedUtc));
        Save();
    }

    /// <returns>False when nothing was kept under that path.</returns>
    public bool Unkeep(string fullPath)
    {
        var removed = _entries.RemoveAll(entry => SamePath(entry.FullPath, fullPath)) > 0;
        if (removed) Save();
        return removed;
    }

    /// <summary>Follows a file TidyBox moved, so tidying it does not make it forget it was kept.</summary>
    public void Rename(string oldPath, string newPath)
    {
        var changed = false;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!SamePath(_entries[i].FullPath, oldPath)) continue;

            _entries[i] = _entries[i] with { FullPath = newPath };
            changed = true;
        }

        if (changed) Save();
    }

    public IReadOnlyList<KeptEntry> All() => _entries.ToList();

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(KeptEntry entry, DownloadFile file)
        => SamePath(entry.FullPath, file.FullPath) && entry.SizeBytes == file.SizeBytes && entry.ModifiedUtc.Ticks == file.ModifiedUtc.Ticks;

    private List<KeptEntry> Load()
    {
        if (!File.Exists(_filePath)) return new List<KeptEntry>();

        try
        {
            return JsonSerializer.Deserialize<List<KeptEntry>>(File.ReadAllText(_filePath)) ?? new List<KeptEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A kept list is a convenience; a damaged file just means nothing is kept yet.
            return new List<KeptEntry>();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries));
    }
}
